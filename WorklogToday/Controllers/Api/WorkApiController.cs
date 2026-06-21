using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorklogToday.Data;
using WorklogToday.Models.Domain;
using WorklogToday.Services;

namespace WorklogToday.Controllers.Api;

[ApiController]
[Authorize]
[IgnoreAntiforgeryToken]
[Route("api/work")]
public class WorkApiController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAiService _ai;
    private readonly IEmailService _email;

    public WorkApiController(ApplicationDbContext db, UserManager<ApplicationUser> users, IAiService ai, IEmailService email)
    {
        _db = db;
        _users = users;
        _ai = ai;
        _email = email;
    }

    public record WorkDto(string Task, string? Project, int Category, int Status, double Hours, string Date, bool Billable, string? Notes);

    private string Uid => _users.GetUserId(User)!;

    private static object Shape(WorkEntry w) => new
    {
        w.Id, w.Task, w.Project,
        category = (int)w.Category, categoryName = w.Category.ToString(),
        status = (int)w.Status, statusName = w.Status.ToString(),
        w.Hours, date = w.Date.ToString("yyyy-MM-dd"), w.Billable, w.Notes
    };

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] WorkDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Task)) return BadRequest(new { error = "Task is required." });
        var entry = new WorkEntry
        {
            UserId = Uid,
            Task = dto.Task.Trim(),
            Project = string.IsNullOrWhiteSpace(dto.Project) ? null : dto.Project!.Trim(),
            Category = (WorkCategory)dto.Category,
            Status = (WorkStatus)dto.Status,
            Hours = Math.Clamp(dto.Hours, 0, 24),
            Date = ParseDate(dto.Date),
            Billable = dto.Billable,
            Notes = dto.Notes
        };
        _db.WorkEntries.Add(entry);
        await _db.SaveChangesAsync();
        return Ok(Shape(entry));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] WorkDto dto)
    {
        var entry = await Find(id);
        if (entry == null) return NotFound();
        entry.Task = dto.Task.Trim();
        entry.Project = string.IsNullOrWhiteSpace(dto.Project) ? null : dto.Project!.Trim();
        entry.Category = (WorkCategory)dto.Category;
        entry.Status = (WorkStatus)dto.Status;
        entry.Hours = Math.Clamp(dto.Hours, 0, 24);
        entry.Date = ParseDate(dto.Date);
        entry.Billable = dto.Billable;
        entry.Notes = dto.Notes;
        await _db.SaveChangesAsync();
        return Ok(Shape(entry));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await Find(id);
        if (entry == null) return NotFound();
        _db.WorkEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(string from, string to, CancellationToken ct)
    {
        var f = ParseDate(from);
        var t = ParseDate(to);
        var entries = await _db.WorkEntries
            .Where(w => w.UserId == Uid && w.Date >= f && w.Date <= t)
            .OrderBy(w => w.Date).ToListAsync();
        var label = $"{f:dd MMM} – {t:dd MMM yyyy}";
        var result = await _ai.SummarizeWorkAsync(entries, label, ct);
        return Ok(new { text = result.Text, source = result.Source, hours = entries.Sum(e => e.Hours), count = entries.Count });
    }

    [HttpGet("standup")]
    public async Task<IActionResult> Standup(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var since = today.AddDays(-3);
        var entries = await _db.WorkEntries
            .Where(w => w.UserId == Uid && w.Date >= since && w.Date <= today)
            .OrderBy(w => w.Date).ToListAsync();
        var result = await _ai.GenerateStandupAsync(entries, ct);
        return Ok(new { text = result.Text, source = result.Source });
    }

    [HttpPost("suggest")]
    public async Task<IActionResult> Suggest([FromBody] SuggestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Task)) return BadRequest(new { error = "Task required." });
        var result = await _ai.SuggestTaskDetailsAsync(dto.Task, ct);
        return Ok(new { category = result.Category, hours = result.Hours, source = result.Source });
    }

    public record SuggestDto(string Task);

    [HttpGet("retro")]
    public async Task<IActionResult> Retro(string from, string to, CancellationToken ct)
    {
        var f = ParseDate(from);
        var t = ParseDate(to);
        var entries = await _db.WorkEntries
            .Where(w => w.UserId == Uid && w.Date >= f && w.Date <= t)
            .OrderBy(w => w.Date).ToListAsync();
        var result = await _ai.GenerateRetroAsync(entries, ct);
        return Ok(new { text = result.Text, source = result.Source });
    }

    [HttpGet("productivity")]
    public async Task<IActionResult> Productivity(string from, string to, CancellationToken ct)
    {
        var f = ParseDate(from);
        var t = ParseDate(to);
        var entries = await _db.WorkEntries
            .Where(w => w.UserId == Uid && w.Date >= f && w.Date <= t)
            .OrderBy(w => w.Date).ToListAsync();
        var result = await _ai.GenerateProductivityInsightAsync(entries, ct);
        return Ok(new { text = result.Text, source = result.Source });
    }

    [HttpGet("status-update")]
    public async Task<IActionResult> StatusUpdate(string from, string to, string format = "email", CancellationToken ct = default)
    {
        var f = ParseDate(from);
        var t = ParseDate(to);
        var entries = await _db.WorkEntries
            .Where(w => w.UserId == Uid && w.Date >= f && w.Date <= t)
            .OrderBy(w => w.Date).ToListAsync();
        var result = await _ai.GenerateStatusUpdateAsync(entries, format, ct);
        return Ok(new { text = result.Text, source = result.Source });
    }

    [HttpGet("report")]
    public async Task<IActionResult> Report(string from, string to)
    {
        var f = ParseDate(from);
        var t = ParseDate(to);
        var entries = await _db.WorkEntries
            .Where(w => w.UserId == Uid && w.Date >= f && w.Date <= t).ToListAsync();

        return Ok(new
        {
            totalHours = entries.Sum(e => e.Hours),
            billableHours = entries.Where(e => e.Billable).Sum(e => e.Hours),
            byProject = entries.GroupBy(e => string.IsNullOrWhiteSpace(e.Project) ? "General" : e.Project!)
                .Select(g => new { project = g.Key, hours = g.Sum(e => e.Hours) }).OrderByDescending(x => x.hours),
            byCategory = entries.GroupBy(e => e.Category)
                .Select(g => new { category = g.Key.ToString(), hours = g.Sum(e => e.Hours) }).OrderByDescending(x => x.hours),
            byDay = entries.GroupBy(e => e.Date)
                .Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), hours = g.Sum(e => e.Hours) }).OrderBy(x => x.date)
        });
    }

    public record EmailManagerDto(string ManagerEmail, string? Message, string From, string To);

    [HttpPost("email-manager")]
    public async Task<IActionResult> EmailManager([FromBody] EmailManagerDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ManagerEmail))
            return BadRequest(new { error = "Manager email is required." });
        if (!_email.IsConfigured)
            return StatusCode(503, new { error = "Email is not configured on this server. Add SMTP settings to appsettings.json." });

        var user = await _users.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var f = ParseDate(dto.From);
        var t = ParseDate(dto.To);
        var entries = await _db.WorkEntries
            .Where(w => w.UserId == Uid && w.Date >= f && w.Date <= t)
            .OrderBy(w => w.Date).ToListAsync();

        var weekLabel = $"{f:dd MMM} – {t:dd MMM yyyy}";
        var html = BuildReportEmail(user.FullName, entries, weekLabel, dto.Message?.Trim());

        await _email.SendAsync(
            dto.ManagerEmail.Trim(), "Manager",
            $"Weekly work report — {user.FullName} ({weekLabel})",
            html, ct);

        return Ok(new { ok = true });
    }

    private static string BuildReportEmail(string userName, List<WorkEntry> entries, string weekLabel, string? message)
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

        var totalHours = entries.Sum(e => e.Hours).ToString("0.#");
        var billable = entries.Where(e => e.Billable).Sum(e => e.Hours).ToString("0.#");
        var count = entries.Count;

        var rows = string.Concat(entries.Select(e =>
            $"<tr><td>{H(e.Date.ToString("ddd dd MMM"))}</td>" +
            $"<td><strong>{H(e.Task)}</strong>{(string.IsNullOrWhiteSpace(e.Project) ? "" : $" <span style='color:#94a3b8;font-size:12px'>· {H(e.Project)}</span>")}</td>" +
            $"<td><span class='cat'>{H(e.Category.ToString())}</span></td>" +
            $"<td><strong>{e.Hours.ToString("0.#")}h</strong></td></tr>"));

        var msgBlock = string.IsNullOrWhiteSpace(message) ? "" :
            $"<div class='msg'><strong>Message from {H(userName)}:</strong><br>{H(message)}</div>";

        const string css = """
            body{font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Arial,sans-serif;background:#f8fafc;margin:0;padding:32px}
            .wrap{max-width:620px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,.08)}
            .hd{background:#0f172a;padding:32px;color:#fff}
            .logo{color:#f59e0b;font-weight:700;font-size:16px;margin-bottom:8px}
            h1{margin:0 0 4px;font-size:22px;color:#fff}
            .sub{color:rgba(255,255,255,.55);font-size:13px}
            .stats{display:flex;padding:24px 32px;gap:0}
            .stat{flex:1;text-align:center;border-right:1px solid #f1f5f9}
            .stat:last-child{border-right:none}
            .sv{font-size:30px;font-weight:800;color:#0f172a}
            .sl{font-size:11px;color:#94a3b8;margin-top:2px;text-transform:uppercase;letter-spacing:.04em}
            .msg{padding:16px 32px;background:#fef9ee;border-top:1px solid #fde68a;border-bottom:1px solid #fde68a;font-size:13.5px;color:#78350f;line-height:1.6}
            table{width:100%;border-collapse:collapse}
            th{background:#f8fafc;padding:10px 20px;text-align:left;font-size:11px;color:#64748b;font-weight:600;text-transform:uppercase;letter-spacing:.05em;border-bottom:1px solid #e2e8f0}
            td{padding:12px 20px;font-size:13.5px;color:#334155;border-bottom:1px solid #f1f5f9}
            .cat{display:inline-block;padding:2px 8px;border-radius:100px;font-size:11px;font-weight:600;background:#dbeafe;color:#1e40af}
            .ft{background:#f8fafc;padding:20px 32px;text-align:center;font-size:12px;color:#94a3b8;border-top:1px solid #f1f5f9}
            .ft a{color:#f59e0b;text-decoration:none;font-weight:600}
            """;

        return "<!DOCTYPE html><html><head><meta charset='utf-8'><style>" + css + "</style></head><body>" +
            "<div class='wrap'>" +
            "<div class='hd'><div class='logo'>worklog.today</div><h1>Weekly Work Report</h1>" +
            $"<div class='sub'>{H(userName)} &middot; {H(weekLabel)}</div></div>" +
            "<div class='stats'>" +
            $"<div class='stat'><div class='sv'>{totalHours}h</div><div class='sl'>Total hours</div></div>" +
            $"<div class='stat'><div class='sv'>{billable}h</div><div class='sl'>Billable</div></div>" +
            $"<div class='stat'><div class='sv'>{count}</div><div class='sl'>Tasks</div></div>" +
            "</div>" + msgBlock +
            "<table><thead><tr><th>Date</th><th>Task</th><th>Category</th><th>Hrs</th></tr></thead>" +
            $"<tbody>{rows}</tbody></table>" +
            $"<div class='ft'>Sent by <strong>{H(userName)}</strong> via <a href='https://worklog.today'>worklog.today</a> &mdash; AI-powered work tracking for professionals</div>" +
            "</div></body></html>";
    }

    private Task<WorkEntry?> Find(int id) =>
        _db.WorkEntries.FirstOrDefaultAsync(w => w.Id == id && w.UserId == Uid);

    private static DateTime ParseDate(string? s) =>
        DateTime.TryParse(s, out var d) ? d.Date : DateTime.UtcNow.Date;
}
