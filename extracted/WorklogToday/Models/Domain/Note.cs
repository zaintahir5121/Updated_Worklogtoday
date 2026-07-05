using System.ComponentModel.DataAnnotations;

namespace WorklogToday.Models.Domain;

public class Note
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [StringLength(200)]
    public string? Title { get; set; }

    [StringLength(8000)]
    public string Content { get; set; } = string.Empty;

    [StringLength(9)]
    public string ColorHex { get; set; } = "#ffffff";

    [StringLength(200)]
    public string? Labels { get; set; }

    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
