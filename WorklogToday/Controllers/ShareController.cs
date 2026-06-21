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
