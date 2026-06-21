using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorklogToday.Data;
using WorklogToday.Models.Domain;
using WorklogToday.Models.ViewModels;

namespace WorklogToday.Controllers;

[Authorize]
public class AppController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public AppController(ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<IActionResult> Index(int week = 0)
    {
        var userId = _users.GetUserId(User)!;
        var user = await _users.GetUserAsync(User);
        if (user == null)
        {
            await HttpContext.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

        var notes = await _db.Notes
            .Where(n => n.UserId == userId && !n.IsArchived)
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync();

        var (start, end) = WeekRange(week);
        var entries = await _db.WorkEntries
            .Where(w => w.UserId == userId && w.Date >= start && w.Date <= end)
            .OrderBy(w => w.Date).ThenBy(w => w.Id)
            .ToListAsync();

        var labels = notes
            .Where(n => !string.IsNullOrWhiteSpace(n.Labels))
            .SelectMany(n => n.Labels!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(l => l.ToLowerInvariant())
            .Distinct().OrderBy(l => l).ToList();

        // Calculate week streak (consecutive weeks with at least one entry)
        var allEntryDates = await _db.WorkEntries
            .Where(w => w.UserId == userId)
            .Select(w => w.Date)
            .ToListAsync();

        var weeksWithEntries = allEntryDates.Select(ToMonday).ToHashSet();
        var thisMonday = ToMonday(DateTime.UtcNow.Date);
        int streak = 0;
        var checkMonday = thisMonday;
        while (weeksWithEntries.Contains(checkMonday)) { streak++; checkMonday = checkMonday.AddDays(-7); }
        // If this week has no entries yet, count streak from last week so we don't break it
        if (streak == 0) { checkMonday = thisMonday.AddDays(-7); while (weeksWithEntries.Contains(checkMonday)) { streak++; checkMonday = checkMonday.AddDays(-7); } }

        var vm = new AppViewModel
        {
            User = user,
            PinnedNotes = notes.Where(n => n.IsPinned).ToList(),
            OtherNotes = notes.Where(n => !n.IsPinned).ToList(),
            AllLabels = labels,
            WeekStart = start,
            WeekEnd = end,
            WeekOffset = week,
            WeekEntries = entries,
            NoteCount = notes.Count,
            WeekStreak = streak
        };
        return View(vm);
    }

    internal static DateTime ToMonday(DateTime d)
    {
        int diff = (7 + (int)d.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return d.AddDays(-diff).Date;
    }

    private static (DateTime start, DateTime end) WeekRange(int offset)
    {
        var today = DateTime.UtcNow.Date;
        int diff = (7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        var monday = today.AddDays(-diff).AddDays(offset * 7);
        return (monday, monday.AddDays(6));
    }
}
