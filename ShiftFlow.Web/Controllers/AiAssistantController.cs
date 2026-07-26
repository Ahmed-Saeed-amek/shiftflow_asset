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
    public async Task<IActionResult> Query([FromBody] AiQueryRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { error = _loc.T("Text is required") });

        if (req.Text.Length > 500)
            return BadRequest(new { error = _loc.T("Text exceeds maximum length of 500 characters") });

        var userId = _um.GetUserId(User)!;
        var history = req.History?.TakeLast(12).ToList();

        try
        {
            var answer = await _orchestrator.RunAsync(req.Text, history, userId, _loc.Lang, ct);
            var voice = _loc.Lang == "ar" ? ArabicVoice : _aiOpts.DefaultVoice;
            return Ok(new { answerText = answer, voice });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI assistant query failed for text: {Text}", req.Text[..Math.Min(50, req.Text.Length)]);
            return StatusCode(500, new { error = _loc.T("An error occurred processing your request. Please try again.") });
        }
    }
}

public record AiQueryRequest(string Text, List<ConversationTurn>? History);
