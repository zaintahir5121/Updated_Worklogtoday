namespace WorklogToday.Services;

public interface IEmailService
{
    Task SendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct = default);
    bool IsConfigured { get; }
}
