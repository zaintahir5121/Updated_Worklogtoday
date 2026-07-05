using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorklogToday.Data;
using WorklogToday.Models.Domain;

namespace WorklogToday.Controllers;

[Authorize]
[Route("sticky")]
public class StickyController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public StickyController(ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    [HttpGet("new")]
    public async Task<IActionResult> New(string? color)
    {
        var note = new Note
        {
            UserId = _users.GetUserId(User)!,
            Content = string.Empty,
            ColorHex = string.IsNullOrWhiteSpace(color) ? "#fff8c5" : color!,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Notes.Add(note);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { id = note.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Index(int id)
    {
        var uid = _users.GetUserId(User)!;
        var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == uid);
        if (note == null) return NotFound();
        return View(note);
    }
}
