using System.Net;
using System.Net.Mail;

namespace WorklogToday.Services;

public class EmailService : IEmailService
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _user;
    private readonly string _password;
    private readonly string _from;
    private readonly string _fromName;
    private readonly ILogger<EmailService> _logger;

    public bool IsConfigured { get; }

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _logger = logger;
        _host = config["Smtp:Host"] ?? "";
        _port = int.TryParse(config["Smtp:Port"], out var p) ? p : 587;
        _user = config["Smtp:User"] ?? "";
        _password = config["Smtp:Password"] ?? "";
        _from = config["Smtp:From"] ?? "noreply@worklog.today";
        _fromName = config["Smtp:FromName"] ?? "worklog.today";
        IsConfigured = !string.IsNullOrWhiteSpace(_host) && !string.IsNullOrWhiteSpace(_user);
    }

    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("SMTP not configured — skipping email to {Email}", toEmail);
            return;
        }

        using var client = new SmtpClient(_host, _port)
        {
            Credentials = new NetworkCredential(_user, _password),
            EnableSsl = true
        };

        using var msg = new MailMessage
        {
            From = new MailAddress(_from, _fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        msg.To.Add(new MailAddress(toEmail, toName));

        try
        {
            await client.SendMailAsync(msg, ct);
            _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
        }
    }
}
