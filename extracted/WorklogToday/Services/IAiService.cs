using WorklogToday.Models.Domain;

namespace WorklogToday.Services;

public record AiResponse(string Text, string Source);

public interface IAiService
{
    Task<AiResponse> SummarizeWorkAsync(IReadOnlyList<WorkEntry> entries, string periodLabel, CancellationToken ct = default);
    Task<AiResponse> SuggestLabelsAsync(string title, string content, CancellationToken ct = default);
}
