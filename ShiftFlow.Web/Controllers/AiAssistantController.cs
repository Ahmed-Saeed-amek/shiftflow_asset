using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ShiftFlow.Application.AI;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.Localization;

namespace ShiftFlow.Web.Controllers;

[Authorize(Policy = PermissionCatalog.AiAssistantUse)]
[EnableRateLimiting("ai")]
public class AiAssistantController : Controller
{
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static (string Token, DateTime Expiry) _cachedToken;

    private const string ArabicVoice = "ar-SA-ZariyahNeural";
    private const int MaxTextLength = 500;
    private const int MaxHistoryTurns = 12;
    private const int MaxHistoryTurnLength = 2000;
    private const int MaxHistoryBytes = 16 * 1024;
    private const int MaxTokenLength = 64;

    private readonly AiAssistantOrchestrator _orchestrator;
    private readonly AzureSpeechOptions _speechOpts;
    private readonly AiAssistantOptions _aiOpts;
    private readonly IHttpClientFactory _httpFactory;
    private readonly UserManager<ApplicationUser> _um;
    private readonly ILanguageService _loc;
    private readonly ILogger<AiAssistantController> _logger;

    public AiAssistantController(
        AiAssistantOrchestrator orchestrator,
        IOptions<AzureSpeechOptions> speechOpts,
        IOptions<AiAssistantOptions> aiOpts,
        IHttpClientFactory httpFactory,
        UserManager<ApplicationUser> um,
        ILanguageService loc,
        ILogger<AiAssistantController> logger)
    {
        _orchestrator = orchestrator;
        _speechOpts = speechOpts.Value;
        _aiOpts = aiOpts.Value;
        _httpFactory = httpFactory;
        _um = um;
        _loc = loc;
        _logger = logger;
    }

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> SpeechToken(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_speechOpts.Key))
            return StatusCode(503, new { error = "Speech service not configured" });

        await TokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken.Token is not null && DateTime.UtcNow < _cachedToken.Expiry)
                return Ok(new { token = _cachedToken.Token, region = _speechOpts.Region });

            var client = _httpFactory.CreateClient("SpeechToken");
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://{_speechOpts.Region}.api.cognitive.microsoft.com/sts/v1.0/issueToken");
            request.Headers.Add("Ocp-Apim-Subscription-Key", _speechOpts.Key);

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Speech token fetch failed: {Status}", response.StatusCode);
                return StatusCode(502, new { error = "Failed to fetch speech token" });
            }

            var token = await response.Content.ReadAsStringAsync(ct);
            _cachedToken = (token, DateTime.UtcNow.AddMinutes(9));
            return Ok(new { token, region = _speechOpts.Region });
        }
        finally
        {
            TokenLock.Release();
        }
    }

    // Relay/ICE credentials for the real-time WebRTC Avatar session (Azure AI Speech's
    // "Talking Avatar" feature — a video of a character lip-syncing the TTS output, distinct
    // from the plain audio-only SpeechToken above). Same pattern as SpeechToken: short-lived,
    // fetched fresh per session rather than cached, since ICE credentials are tied to the
    // WebRTC connection being established right after.
    [HttpGet]
    public async Task<IActionResult> AvatarIceToken(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_speechOpts.Key))
            return StatusCode(503, new { error = "Speech service not configured" });

        var client = _httpFactory.CreateClient("SpeechToken");
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://{_speechOpts.Region}.tts.speech.microsoft.com/cognitiveservices/avatar/relay/token/v1");
        request.Headers.Add("Ocp-Apim-Subscription-Key", _speechOpts.Key);

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Avatar ICE token fetch failed: {Status} {Body}", response.StatusCode, body);
            return StatusCode(502, new { error = "Failed to fetch avatar ICE token" });
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return Content(json, "application/json");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Query([FromBody] AiQueryRequest? req, CancellationToken ct)
    {
        // This controller extends Controller (not [ApiController]/ControllerBase), so ASP.NET
        // Core does not auto-400 a missing/malformed JSON body — req can reach here as null,
        // which would otherwise crash with a raw NullReferenceException on the line below.
        if (req is null || string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { error = _loc.T("Text is required") });

        if (req.Text.Length > MaxTextLength)
            return BadRequest(new { error = _loc.T("Text exceeds maximum length of 500 characters") });

        // The history is entirely client-supplied, so it is capped the same way req.Text is:
        // per-turn length, total size, and an allow-list of roles (anything else would let a
        // caller inject a "system"-looking turn or push an unbounded payload at the model).
        List<ConversationTurn>? history = null;
        if (req.History is { Count: > 0 })
        {
            var turns = req.History.TakeLast(MaxHistoryTurns).ToList();
            var totalBytes = 0;
            history = new List<ConversationTurn>(turns.Count);
            foreach (var turn in turns)
            {
                var text = turn.Text ?? string.Empty;
                if (text.Length > MaxHistoryTurnLength)
                    return BadRequest(new { error = _loc.T("Conversation history is too large. Please clear the chat and try again.") });

                totalBytes += System.Text.Encoding.UTF8.GetByteCount(text);
                if (totalBytes > MaxHistoryBytes)
                    return BadRequest(new { error = _loc.T("Conversation history is too large. Please clear the chat and try again.") });

                var role = turn.Role == "assistant" ? "assistant" : "user";
                history.Add(new ConversationTurn(role, text));
            }
        }

        var userId = _um.GetUserId(User)!;

        try
        {
            var result = await _orchestrator.RunAsync(req.Text, history, userId, _loc.Lang, SanitizeContext(req.Context), ct);
            return Ok(new { answerText = result.Text, voice = Voice, attachments = result.Attachments });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI assistant query failed for text: {Text}", req.Text[..Math.Min(50, req.Text.Length)]);
            return StatusCode(500, new { error = _loc.T("An error occurred processing your request. Please try again.") });
        }
    }

    /// <summary>Carries out an action the assistant proposed and the user confirmed. The token is
    /// single-use, expires after 10 minutes, must belong to the caller, and is re-checked against
    /// the permission the proposing tool recorded — so a token leaked into someone else's session
    /// is inert, and a permission revoked in between stops the action.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm([FromBody] AiConfirmRequest? req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Token) || req.Token.Length > MaxTokenLength)
            // Deliberately not localized: Translations.cs is owned by another change, and this
            // is a malformed-request guard the UI never surfaces (the client only posts tokens it
            // was just handed). See TRANSLATIONS-ai-backend.md for the string to localize on merge.
            return BadRequest(new { error = "Confirmation token is required" });

        var userId = _um.GetUserId(User)!;
        try
        {
            var result = await _orchestrator.ConfirmAsync(req.Token, userId, _loc.Lang, ct);
            return Ok(new { answerText = result.Text, voice = Voice, attachments = result.Attachments });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI assistant confirm failed");
            return StatusCode(500, new { error = _loc.T("An error occurred processing your request. Please try again.") });
        }
    }

    /// <summary>Throws away a pending action the user declined. Returns 200 either way — a token
    /// that already expired is, from the caller's point of view, already dismissed.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Dismiss([FromBody] AiConfirmRequest? req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Token) || req.Token.Length > MaxTokenLength)
            return BadRequest(new { error = "Confirmation token is required" });

        _orchestrator.DiscardPending(req.Token, _um.GetUserId(User)!);
        return Ok(new { ok = true });
    }

    private string Voice => _loc.Lang == "ar" ? ArabicVoice : _aiOpts.DefaultVoice;

    /// <summary>The page context is client-supplied, so it is capped and its entityType is
    /// allow-listed before it reaches the system prompt — it must not become a free-text injection
    /// channel into the model's instructions.</summary>
    private static AiPageContext? SanitizeContext(AiQueryContext? context)
    {
        if (context == null) return null;
        var entityType = context.EntityType != null && AllowedEntityTypes.Contains(context.EntityType)
            ? context.EntityType : null;
        return new AiPageContext(
            Cap(context.Page, 200),
            entityType,
            entityType == null ? null : Cap(EntityIdText(context.EntityId), 64),
            Cap(context.Title, 200));
    }

    private static readonly HashSet<string> AllowedEntityTypes = new(StringComparer.Ordinal)
    {
        "WorkOrder", "InspectionOrder", "MaintenanceOrder", "Asset", "Zone", "SparePart",
        "Contract", "Vendor", "User", "Group",
    };

    /// <summary>entityId arrives as either a JSON string ("WO-2026-0004") or a number (4) — the
    /// contract allows both, and binding a number straight into a string property would 400 the
    /// whole request, so it is read as a JsonElement and flattened here.</summary>
    private static string? EntityIdText(System.Text.Json.JsonElement? entityId) => entityId?.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String => entityId.Value.GetString(),
        System.Text.Json.JsonValueKind.Number => entityId.Value.GetRawText(),
        _ => null,
    };

    private static string? Cap(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

public record AiQueryRequest(string Text, List<ConversationTurn>? History, AiQueryContext? Context);

/// <summary>Where the user is when they ask. EntityId is a string so the client can send either a
/// numeric id or an order number without guessing which the backend wants.</summary>
public record AiQueryContext(string? Page, string? EntityType, System.Text.Json.JsonElement? EntityId, string? Title);

public record AiConfirmRequest(string Token);
