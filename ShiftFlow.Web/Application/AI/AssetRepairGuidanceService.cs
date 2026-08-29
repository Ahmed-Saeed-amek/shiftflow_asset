using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShiftFlow.Application.Services;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.AI;

/// <summary>
/// Backs the getAssetRepairGuidance AI tool. Deliberately NOT a general "search YouTube" capability —
/// every safeguard below exists to keep it that way:
///   - The only input is assetId (an int, validated against the DB) — the model can never pass free
///     text into the search, so it can't be repurposed as an arbitrary video-search proxy.
///   - The search query is built entirely server-side from that asset's own Manufacturer/Model/
///     Category/Name fields, sanitized to plain word tokens.
///   - Requires the caller to actually have Asset.View and to be within their assigned asset scope
///     (same Zone/LocationCategory/Category check as the Assets list page).
///   - A dedicated per-user rate limit (IYouTubeQuotaTracker), independent of the chat endpoint's
///     own limiter, bounds calls to this specific paid/quota-limited external API.
///   - Returned video links are always reconstructed from a regex-validated 11-character video ID —
///     never a URL taken directly from the API response.
///   - Every failure mode (no API key configured, rate-limited, HTTP/parse error) degrades to
///     "no video found" plus the asset's own metadata, so the assistant still has what it needs to
///     give general text guidance instead of failing the whole chat turn.
/// </summary>
public class AssetRepairGuidanceService : IAssetRepairGuidanceService
{
    private static readonly Regex VideoIdPattern = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly YouTubeOptions _opts;
    private readonly IYouTubeQuotaTracker _quota;
    private readonly IAssetScopeService _scopeService;
    private readonly ILogger<AssetRepairGuidanceService> _logger;

    public AssetRepairGuidanceService(ApplicationDbContext db, IHttpClientFactory httpFactory,
        IOptions<YouTubeOptions> opts, IYouTubeQuotaTracker quota, IAssetScopeService scopeService, ILogger<AssetRepairGuidanceService> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _opts = opts.Value;
        _quota = quota;
        _scopeService = scopeService;
        _logger = logger;
    }

    public async Task<object> GetRepairGuidanceAsync(int assetId, string userId, CancellationToken ct)
    {
        var asset = await _db.Assets.AsNoTracking()
            .Include(a => a.Category)
            .Include(a => a.Zone)
            .FirstOrDefaultAsync(a => a.Id == assetId, ct);
        if (asset == null)
            return new { error = "not_found", message = "Asset not found." };

        if (!await _scopeService.IsInScopeAsync(asset, userId))
            return new { error = "forbidden", message = "This asset is outside your assigned scope." };

        var assetInfo = new
        {
            name = asset.Name,
            category = asset.Category?.Name,
            manufacturer = asset.Manufacturer,
            model = asset.Model,
            sku = asset.Sku,
            status = asset.Status,
        };

        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            return new { videoFound = false, reason = "not_configured", asset = assetInfo };

        if (!_quota.TryConsume(userId))
            return new { videoFound = false, reason = "rate_limited", asset = assetInfo };

        var queryParts = new[] { asset.Manufacturer, asset.Model, asset.Category?.Name, asset.Name }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => SanitizeQueryPart(p!));
        var query = (string.Join(" ", queryParts) + " repair replace how to fix tutorial").Trim();
        if (query.Length > 120) query = query[..120];

        try
        {
            var client = _httpFactory.CreateClient("YouTube");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

            // The API key travels as a header, not a query-string parameter — HttpClient's
            // default logging handler logs the full request URI at Information level (visible
            // in this app's Serilog file sink), so a key in the URL would end up in plaintext
            // log files. Headers aren't logged at that level, so this keeps it out of logs.
            var url = "search" +
                "?part=snippet&type=video&maxResults=3&safeSearch=strict&videoEmbeddable=true" +
                $"&q={Uri.EscapeDataString(query)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-goog-api-key", _opts.ApiKey);
            using var response = await client.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("YouTube search failed for asset {AssetId}: {Status}", assetId, response.StatusCode);
                return new { videoFound = false, reason = "search_failed", asset = assetInfo };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);

            var videos = new List<object>();
            if (doc.RootElement.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (videos.Count >= 3) break;
                    if (!item.TryGetProperty("id", out var idEl) || !idEl.TryGetProperty("videoId", out var videoIdEl))
                        continue;

                    var videoId = videoIdEl.GetString() ?? "";
                    if (!VideoIdPattern.IsMatch(videoId)) continue;

                    var title = "";
                    var channel = "";
                    if (item.TryGetProperty("snippet", out var snippet))
                    {
                        title = SanitizeDisplayText(snippet.TryGetProperty("title", out var t) ? t.GetString() : null);
                        channel = SanitizeDisplayText(snippet.TryGetProperty("channelTitle", out var c) ? c.GetString() : null);
                    }

                    videos.Add(new
                    {
                        title,
                        channel,
                        url = $"https://www.youtube.com/watch?v={videoId}",
                    });
                }
            }

            return new { videoFound = videos.Count > 0, videos, asset = assetInfo };
        }
        catch (OperationCanceledException)
        {
            return new { videoFound = false, reason = "search_failed", asset = assetInfo };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YouTube search errored for asset {AssetId}", assetId);
            return new { videoFound = false, reason = "search_failed", asset = assetInfo };
        }
    }

    private static string SanitizeQueryPart(string value)
    {
        var cleaned = Regex.Replace(value, @"[^\w\s-]", " ", RegexOptions.None, TimeSpan.FromMilliseconds(200));
        return Regex.Replace(cleaned, @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(200)).Trim();
    }

    private static string SanitizeDisplayText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var noControlChars = new string(value.Where(ch => !char.IsControl(ch)).ToArray());
        return noControlChars.Length > 150 ? noControlChars[..150] : noControlChars;
    }
}
