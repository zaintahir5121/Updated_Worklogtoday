using WorklogToday.Models.Domain;

namespace WorklogToday.Services;

public record AiResponse(string Text, string Source);
public record AiTaskSuggestion(int Category, double Hours, string Source);
public record AiExtractedTask(string Task, int Category, double Hours);

public interface IAiService
{
    Task<AiResponse> SummarizeWorkAsync(IReadOnlyList<WorkEntry> entries, string periodLabel, CancellationToken ct = default);
    Task<AiResponse> SuggestLabelsAsync(string title, string content, CancellationToken ct = default);
    Task<AiTaskSuggestion> SuggestTaskDetailsAsync(string taskDescription, CancellationToken ct = default);
    Task<AiResponse> GenerateStandupAsync(IReadOnlyList<WorkEntry> entries, CancellationToken ct = default);
    Task<(List<AiExtractedTask> Tasks, string Source)> ExtractTasksAsync(string? title, string content, CancellationToken ct = default);
}
