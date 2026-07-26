using System.Net;
using System.Net.Mail;

namespace ShiftFlow.Web.Services;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody);
}

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _log;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> log)
    {
        _config = config;
        _log = log;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var section = _config.GetSection("Smtp");
        var host = section["Host"] ?? "localhost";
        var port = int.TryParse(section["Port"], out var p) ? p : 25;
        var user = section["User"];
        var pass = section["Password"];
        var from = section["From"] ?? "noreply@shiftflow.local";

        using var client = new SmtpClient(host, port);
        client.EnableSsl = bool.TryParse(section["EnableSsl"], out var ssl) && ssl;

        if (!string.IsNullOrEmpty(user))
            client.Credentials = new NetworkCredential(user, pass);

        var msg = new MailMessage(from, to, subject, htmlBody) { IsBodyHtml = true };

        try
        {
            await client.SendMailAsync(msg);
            _log.LogInformation("Email sent to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            // Log and continue — user is created regardless of email failure
            _log.LogError(ex, "Failed to send email to {To}. Subject: {Subject}", to, subject);
        }
    }
}
