using WorklogToday.Models.Domain;

namespace WorklogToday.Models.ViewModels;

public class ShareReceivedModel
{
    public string? Title    { get; set; }
    public string? Body     { get; set; }
    public string? AudioUrl { get; set; }
    public bool    IsAudio  => !string.IsNullOrEmpty(AudioUrl);
}

public class ShareViewModel
{
    public string ShareUserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? JobTitle { get; set; }
    public string? Company { get; set; }
    public string WeekLabel { get; set; } = "";
    public DateTime WeekStart { get; set; }
    public DateTime WeekEnd { get; set; }
    public int WeekOffset { get; set; }
    public double TotalHours { get; set; }
    public double BillableHours { get; set; }
    public int TaskCount { get; set; }
    public List<WorkEntry> Entries { get; set; } = new();
}
