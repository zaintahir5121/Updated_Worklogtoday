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
[AutoValidateAntiforgeryToken]
[Route("api/work")]
public class WorkApiController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAiService _ai;

    public WorkApiController(ApplicationDbContext db, UserManager<ApplicationUser> users, IAiService ai)
    {
        _db = db;
        _users = users;
        _ai = ai;
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

    private Task<WorkEntry?> Find(int id) =>
        _db.WorkEntries.FirstOrDefaultAsync(w => w.Id == id && w.UserId == Uid);

    private static DateTime ParseDate(string? s) =>
        DateTime.TryParse(s, out var d) ? d.Date : DateTime.UtcNow.Date;
}
