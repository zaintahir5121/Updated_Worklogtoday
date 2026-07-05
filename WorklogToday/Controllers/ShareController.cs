using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorklogToday.Data;
using WorklogToday.Models.Domain;
using WorklogToday.Models.ViewModels;

namespace WorklogToday.Controllers;

[AllowAnonymous]
[Route("share")]
public class ShareController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public ShareController(ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    // ── PWA Web Share Target ────────────────────────────────────────────────
    // Called by the OS share sheet when the user picks "worklog.today".
    // Saves any audio to disk then shows the Received page so the user can
    // choose: Save as Note  OR  Log as Task.
    [HttpPost]
    [Authorize]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> Receive(
        [FromForm] string? title,
        [FromForm] string? text,
        [FromForm] string? url,
        IFormFile? audio,
        IFormFile? file,
        CancellationToken ct)
    {
        var uid = _users.GetUserId(User)!;
        var attachment = audio ?? file;
        string? audioUrl = null;

        if (attachment != null && attachment.Length > 0 && IsAudio(attachment.ContentType))
        {
            var ext = AudioExt(attachment.ContentType);
            var fileName = $"share_{uid}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "voice");
            Directory.CreateDirectory(dir);
            using (var fs = System.IO.File.Create(Path.Combine(dir, fileName)))
                await attachment.CopyToAsync(fs, ct);
            audioUrl = $"/uploads/voice/{fileName}";
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(text)) parts.Add(text.Trim());
        if (!string.IsNullOrWhiteSpace(url))  parts.Add(url.Trim());
        var body = string.Join("\n", parts);

        // Save directly as a note — no choice page needed
        var note = new Note
        {
            UserId    = uid,
            Title     = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            Content   = string.IsNullOrWhiteSpace(body) ? (audioUrl != null ? "[Voice note]" : "(shared)") : body,
            AudioUrl  = audioUrl,
            Transcript = audioUrl != null && !string.IsNullOrWhiteSpace(body) ? body : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Notes.Add(note);
        await _db.SaveChangesAsync(ct);
        return Redirect("/app?shared=1");
    }

    private static bool IsAudio(string? ct) =>
        ct != null && (ct.StartsWith("audio/") || ct.Contains("ogg") || ct.Contains("opus"));

    private static string AudioExt(string ct) => ct switch
    {
        var s when s.Contains("webm") => ".webm",
        var s when s.Contains("ogg") || s.Contains("opus") => ".ogg",
        var s when s.Contains("mp4") || s.Contains("m4a") => ".m4a",
        var s when s.Contains("wav") => ".wav",
        var s when s.Contains("mpeg") || s.Contains("mp3") => ".mp3",
        _ => ".audio"
    };
    // ───────────────────────────────────────────────────────────────────────

    [HttpGet("{userId}")]
    public async Task<IActionResult> Week(string userId, int week = 0)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var (start, end) = WeekRange(week);
        var entries = await _db.WorkEntries
            .Where(w => w.UserId == userId && w.Date >= start && w.Date <= end)
            .OrderBy(w => w.Date).ThenBy(w => w.Id)
            .ToListAsync();

        var vm = new ShareViewModel
        {
            ShareUserId = userId,
            UserName = user.FullName,
            JobTitle = user.JobTitle,
            Company = user.Company,
            WeekStart = start,
            WeekEnd = end,
            WeekLabel = $"{start:dd MMM} – {end:dd MMM yyyy}",
            TotalHours = entries.Sum(e => e.Hours),
            BillableHours = entries.Where(e => e.Billable).Sum(e => e.Hours),
            TaskCount = entries.Count,
            Entries = entries,
            WeekOffset = week
        };

        return View("Week", vm);
    }

    private static (DateTime start, DateTime end) WeekRange(int offset)
    {
        var today = DateTime.UtcNow.Date;
        int diff = (7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        var monday = today.AddDays(-diff).AddDays(offset * 7);
        return (monday, monday.AddDays(6));
    }
}
