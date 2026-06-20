using Microsoft.AspNetCore.Identity;

namespace WorklogToday.Models.Domain;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public string? JobTitle { get; set; }
    public string? Company { get; set; }
    public string AvatarColor { get; set; } = "#f59e0b";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Note> Notes { get; set; } = new List<Note>();
    public ICollection<WorkEntry> WorkEntries { get; set; } = new List<WorkEntry>();
}
