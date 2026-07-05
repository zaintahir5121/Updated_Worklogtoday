using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorklogToday.Data;
using WorklogToday.Models.Domain;

namespace WorklogToday.Services;

public class DigestWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmailService _email;
    private readonly ILogger<DigestWorker> _logger;

    public DigestWorker(IServiceScopeFactory scopeFactory, IEmailService email, ILogger<DigestWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _email = email;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            // Fire on Mondays between 07:00–07:05 UTC
            if (now.DayOfWeek == DayOfWeek.Monday && now.Hour == 7 && now.Minute < 5)
            {
                await SendDigestsAsync(stoppingToken);
                // Sleep past the 5-minute window so we don't double-send
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
            else
            {
                // Check every minute
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task SendDigestsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var lastMonday = DateTime.UtcNow.Date.AddDays(-7);
        var lastSunday = lastMonday.AddDays(6);

        var users = await db.Users
            .Where(u => u.EmailDigestEnabled && u.Email != null)
            .ToListAsync(ct);

        _logger.LogInformation("Sending Monday digest to {Count} users", users.Count);

        foreach (var user in users)
        {
            try
            {
                var entries = await db.WorkEntries
                    .Where(w => w.UserId == user.Id && w.Date >= lastMonday && w.Date <= lastSunday)
                    .OrderBy(w => w.Date)
                    .ToListAsync(ct);

                var html = BuildDigestHtml(user, entries, lastMonday, lastSunday);
                await _email.SendAsync(user.Email!, user.FullName,
                    $"Your worklog.today weekly digest — {lastMonday:MMM d} to {lastSunday:MMM d}", html, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Digest failed for user {Id}", user.Id);
            }
        }
    }

    private static string BuildDigestHtml(ApplicationUser user, List<WorkEntry> entries, DateTime from, DateTime to)
    {
        var totalHours = entries.Sum(e => e.Hours);
        var billableHours = entries.Where(e => e.Billable).Sum(e => e.Hours);
        var taskCount = entries.Count;
        var topProject = entries.GroupBy(e => e.Project ?? "General")
            .OrderByDescending(g => g.Sum(e => e.Hours))
            .FirstOrDefault()?.Key ?? "—";

        var rows = string.Join("", entries.Select(e =>
            $"<tr><td style='padding:6px 12px;border-bottom:1px solid #f3f4f6'>{e.Date:ddd dd MMM}</td>" +
            $"<td style='padding:6px 12px;border-bottom:1px solid #f3f4f6'>{System.Net.WebUtility.HtmlEncode(e.Task)}</td>" +
            $"<td style='padding:6px 12px;border-bottom:1px solid #f3f4f6;color:#6b7280'>{e.Project ?? "—"}</td>" +
            $"<td style='padding:6px 12px;border-bottom:1px solid #f3f4f6;text-align:right;font-weight:600'>{e.Hours:0.#}h</td></tr>"));

        var emptyRow = taskCount == 0
            ? "<tr><td colspan='4' style='padding:24px;text-align:center;color:#9ca3af'>No tasks logged last week.</td></tr>"
            : "";

        return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset='utf-8'/></head>
            <body style='margin:0;padding:0;background:#f9fafb;font-family:system-ui,sans-serif'>
              <div style='max-width:600px;margin:32px auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 1px 4px rgba(0,0,0,.08)'>
                <div style='background:#f59e0b;padding:28px 32px'>
                  <div style='font-size:22px;font-weight:700;color:#fff'>worklog<span style='opacity:.7'>.today</span></div>
                  <div style='color:rgba(255,255,255,.85);margin-top:6px;font-size:15px'>Your weekly digest · {from:MMM d} – {to:MMM d, yyyy}</div>
                </div>
                <div style='padding:32px'>
                  <p style='margin:0 0 24px;font-size:16px;color:#111827'>Hi {System.Net.WebUtility.HtmlEncode(user.FullName.Split(' ')[0])}, here's your week at a glance.</p>
                  <div style='display:flex;gap:16px;margin-bottom:28px;flex-wrap:wrap'>
                    <div style='flex:1;min-width:120px;background:#fef3c7;border-radius:10px;padding:16px 20px'>
                      <div style='font-size:28px;font-weight:700;color:#92400e'>{totalHours:0.#}h</div>
                      <div style='font-size:13px;color:#78350f'>Total hours</div>
                    </div>
                    <div style='flex:1;min-width:120px;background:#d1fae5;border-radius:10px;padding:16px 20px'>
                      <div style='font-size:28px;font-weight:700;color:#065f46'>{billableHours:0.#}h</div>
                      <div style='font-size:13px;color:#064e3b'>Billable hours</div>
                    </div>
                    <div style='flex:1;min-width:120px;background:#dbeafe;border-radius:10px;padding:16px 20px'>
                      <div style='font-size:28px;font-weight:700;color:#1e3a8a'>{taskCount}</div>
                      <div style='font-size:13px;color:#1e40af'>Tasks logged</div>
                    </div>
                  </div>
                  {(taskCount > 0 ? $"<p style='font-size:13px;color:#6b7280;margin-bottom:4px'>Top project: <strong style='color:#111827'>{System.Net.WebUtility.HtmlEncode(topProject)}</strong></p>" : "")}
                  <table style='width:100%;border-collapse:collapse;margin-top:20px;font-size:14px'>
                    <thead>
                      <tr style='background:#f9fafb'>
                        <th style='padding:8px 12px;text-align:left;font-size:12px;color:#6b7280;font-weight:600;text-transform:uppercase'>Date</th>
                        <th style='padding:8px 12px;text-align:left;font-size:12px;color:#6b7280;font-weight:600;text-transform:uppercase'>Task</th>
                        <th style='padding:8px 12px;text-align:left;font-size:12px;color:#6b7280;font-weight:600;text-transform:uppercase'>Project</th>
                        <th style='padding:8px 12px;text-align:right;font-size:12px;color:#6b7280;font-weight:600;text-transform:uppercase'>Hours</th>
                      </tr>
                    </thead>
                    <tbody>{rows}{emptyRow}</tbody>
                  </table>
                  <div style='margin-top:32px;text-align:center'>
                    <a href='https://worklog.today/app' style='display:inline-block;background:#f59e0b;color:#fff;text-decoration:none;padding:12px 28px;border-radius:8px;font-weight:600;font-size:15px'>Open worklog.today</a>
                  </div>
                  <p style='margin-top:32px;font-size:12px;color:#9ca3af;text-align:center'>
                    You're receiving this because you enabled weekly digests.<br/>
                    <a href='https://worklog.today/app' style='color:#9ca3af'>Manage preferences in your workspace settings</a>
                  </p>
                </div>
              </div>
            </body>
            </html>
            """;
    }
}
