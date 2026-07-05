using WorklogToday.Models.Domain;

namespace WorklogToday.Models.ViewModels;

public class AppViewModel
{
    public ApplicationUser User { get; set; } = default!;

    public List<Note> PinnedNotes { get; set; } = new();
    public List<Note> OtherNotes { get; set; } = new();
    public List<string> AllLabels { get; set; } = new();

    public DateTime WeekStart { get; set; }
    public DateTime WeekEnd { get; set; }
    public int WeekOffset { get; set; }
    public List<WorkEntry> WeekEntries { get; set; } = new();

    public double TotalHours => WeekEntries.Sum(e => e.Hours);
    public double BillableHours => WeekEntries.Where(e => e.Billable).Sum(e => e.Hours);
    public int EntryCount => WeekEntries.Count;
    public int NoteCount { get; set; }

    public Dictionary<string, double> HoursByProject =>
        WeekEntries.GroupBy(e => string.IsNullOrWhiteSpace(e.Project) ? "General" : e.Project!)
                   .ToDictionary(g => g.Key, g => g.Sum(e => e.Hours));

    public Dictionary<WorkCategory, double> HoursByCategory =>
        WeekEntries.GroupBy(e => e.Category).ToDictionary(g => g.Key, g => g.Sum(e => e.Hours));
}
