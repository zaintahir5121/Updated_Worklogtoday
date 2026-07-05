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
[Route("api/notes")]
public class NotesApiController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAiService _ai;

    public NotesApiController(ApplicationDbContext db, UserManager<ApplicationUser> users, IAiService ai)
    {
        _db = db;
        _users = users;
        _ai = ai;
    }

    public record NoteDto(string? Title, string Content, string? ColorHex, string? Labels);

    private string Uid => _users.GetUserId(User)!;

    private static object Shape(Note n) => new
    {
        n.Id, n.Title, n.Content, n.ColorHex, n.Labels, n.IsPinned, n.IsArchived,
        updatedAt = n.UpdatedAt
    };

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NoteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Content) && string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { error = "Note is empty." });

        var note = new Note
        {
            UserId = Uid,
            Title = dto.Title?.Trim(),
            Content = dto.Content?.Trim() ?? string.Empty,
            ColorHex = string.IsNullOrWhiteSpace(dto.ColorHex) ? "#ffffff" : dto.ColorHex!,
            Labels = NormalizeLabels(dto.Labels),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Notes.Add(note);
        await _db.SaveChangesAsync();
        return Ok(Shape(note));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] NoteDto dto)
    {
        var note = await Find(id);
        if (note == null) return NotFound();
        note.Title = dto.Title?.Trim();
        note.Content = dto.Content?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(dto.ColorHex)) note.ColorHex = dto.ColorHex!;
        note.Labels = NormalizeLabels(dto.Labels);
        note.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(Shape(note));
    }

    [HttpPost("{id:int}/pin")]
    public async Task<IActionResult> TogglePin(int id)
    {
        var note = await Find(id);
        if (note == null) return NotFound();
        note.IsPinned = !note.IsPinned;
        note.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(Shape(note));
    }

    [HttpPost("{id:int}/archive")]
    public async Task<IActionResult> ToggleArchive(int id)
    {
        var note = await Find(id);
        if (note == null) return NotFound();
        note.IsArchived = !note.IsArchived;
        note.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(Shape(note));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var note = await Find(id);
        if (note == null) return NotFound();
        _db.Notes.Remove(note);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("suggest-labels")]
    public async Task<IActionResult> SuggestLabels([FromBody] NoteDto dto, CancellationToken ct)
    {
        var result = await _ai.SuggestLabelsAsync(dto.Title ?? string.Empty, dto.Content ?? string.Empty, ct);
        return Ok(new { labels = result.Text, source = result.Source });
    }

    private Task<Note?> Find(int id) =>
        _db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == Uid);

    private static string? NormalizeLabels(string? labels)
    {
        if (string.IsNullOrWhiteSpace(labels)) return null;
        var parts = labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToLowerInvariant().Trim('#')).Where(p => p.Length > 0).Distinct().Take(6);
        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? null : joined;
    }
}
