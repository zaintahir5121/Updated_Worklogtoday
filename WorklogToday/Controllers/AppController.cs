using System.Globalization;
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
        var user = (await _users.GetUserAsync(User))!;

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
            NoteCount = notes.Count
        };
        return View(vm);
    }

    private static (DateTime start, DateTime end) WeekRange(int offset)
    {
        var today = DateTime.UtcNow.Date;
        int diff = (7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        var monday = today.AddDays(-diff).AddDays(offset * 7);
        return (monday, monday.AddDays(6));
    }
}
