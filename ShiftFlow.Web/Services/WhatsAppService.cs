using System.Net.Http.Headers;
using System.Text;

namespace ShiftFlow.Web.Services;

public interface IWhatsAppService
{
    /// <summary>
    /// Sends a WhatsApp message via Twilio. Phone number must include country code, e.g. +966501234567.
    /// No-ops silently if Twilio is not configured.
    /// </summary>
    Task SendAsync(string toPhone, string message);
}

public class TwilioWhatsAppService : IWhatsAppService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<TwilioWhatsAppService> _log;

    public TwilioWhatsAppService(
        IConfiguration config,
        IHttpClientFactory http,
        ILogger<TwilioWhatsAppService> log)
    {
        _config = config;
        _http = http;
        _log = log;
    }

    public async Task SendAsync(string toPhone, string message)
    {
        var section = _config.GetSection("Twilio");
        var sid = section["AccountSid"];
        var token = section["AuthToken"];
        var from = section["WhatsAppFrom"] ?? "whatsapp:+14155238886";

        if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(token))
        {
            _log.LogWarning("WhatsApp not configured — skipping message to {Phone}", toPhone);
            return;
        }

        // Normalise number: strip spaces, ensure it starts with +
        var normalised = toPhone.Trim().Replace(" ", "");
        if (!normalised.StartsWith('+'))
            normalised = '+' + normalised;

        var to = $"whatsapp:{normalised}";
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json";

        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"]   = to,
            ["From"] = from,
            ["Body"] = message,
        });

        var client = _http.CreateClient();
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        try
        {
            var response = await client.PostAsync(url, body);
            if (response.IsSuccessStatusCode)
                _log.LogInformation("WhatsApp sent to {Phone}", normalised);
            else
            {
                var detail = await response.Content.ReadAsStringAsync();
                _log.LogError("Twilio error {Status} for {Phone}: {Detail}",
                    (int)response.StatusCode, normalised, detail);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send WhatsApp to {Phone}", normalised);
        }
    }
}
