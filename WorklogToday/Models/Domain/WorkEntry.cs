using System.ComponentModel.DataAnnotations;

namespace WorklogToday.Models.Domain;

public enum WorkCategory
{
    Development = 0,
    Meeting = 1,
    Support = 2,
    Review = 3,
    Planning = 4,
    Research = 5,
    Documentation = 6,
    Other = 7
}

public enum WorkStatus
{
    Planned = 0,
    InProgress = 1,
    Done = 2,
    Blocked = 3
}

public class WorkEntry
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;

    [Required]
    [StringLength(280)]
    public string Task { get; set; } = string.Empty;

    [StringLength(120)]
    public string? Project { get; set; }

    public WorkCategory Category { get; set; } = WorkCategory.Development;
    public WorkStatus Status { get; set; } = WorkStatus.Done;

    [Range(0, 24)]
    public double Hours { get; set; } = 1;

    [StringLength(1000)]
    public string? Notes { get; set; }

    public bool Billable { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
