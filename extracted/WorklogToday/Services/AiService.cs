using System.Text;
using System.Text.Json;
using WorklogToday.Models.Domain;

namespace WorklogToday.Services;

/// <summary>
/// AI helper that prefers a local Ollama model when one is reachable, but always
/// falls back to a fast, deterministic local engine so the UX never blocks on a
/// slow/absent model. Provider is controlled via the "Ai:Provider" setting:
/// Auto (default) | Ollama | Local.
/// </summary>
public class AiService : IAiService
{
    private readonly HttpClient _http;
    private readonly ILogger<AiService> _logger;
    private readonly string _provider;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly int _timeoutSeconds;

    public AiService(HttpClient http, IConfiguration config, ILogger<AiService> logger)
    {
        _http = http;
        _logger = logger;
        _provider = config["Ai:Provider"] ?? "Auto";
        _baseUrl = (config["Ai:OllamaBaseUrl"] ?? "http://localhost:11434").TrimEnd('/');
        _model = config["Ai:OllamaModel"] ?? "llama3.2";
        _timeoutSeconds = int.TryParse(config["Ai:TimeoutSeconds"], out var t) ? t : 6;
    }

    public async Task<AiResponse> SummarizeWorkAsync(IReadOnlyList<WorkEntry> entries, string periodLabel, CancellationToken ct = default)
    {
        var local = LocalSummary(entries, periodLabel);

        if (_provider.Equals("Local", StringComparison.OrdinalIgnoreCase) || entries.Count == 0)
            return new AiResponse(local, "local");

        var prompt = BuildSummaryPrompt(entries, periodLabel);
        var ai = await TryOllamaAsync(prompt, ct);
        return ai != null ? new AiResponse(ai, "ollama") : new AiResponse(local, "local");
    }

    public async Task<AiResponse> SuggestLabelsAsync(string title, string content, CancellationToken ct = default)
    {
        var local = LocalLabels(title, content);
        if (_provider.Equals("Local", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(content))
            return new AiResponse(local, "local");

        var prompt = $"Suggest 1-4 short, lowercase topic labels (comma separated, no #) for this note. " +
                     $"Reply with ONLY the labels.\nTitle: {title}\nNote: {content}";
        var ai = await TryOllamaAsync(prompt, ct);
        if (ai == null) return new AiResponse(local, "local");
        var cleaned = string.Join(", ", ai.Replace("\n", ",").Split(',')
            .Select(s => s.Trim().Trim('#', '.', '-').ToLowerInvariant())
            .Where(s => s.Length is > 1 and < 24).Distinct().Take(4));
        return new AiResponse(string.IsNullOrWhiteSpace(cleaned) ? local : cleaned, "ollama");
    }

    // ---------------- Ollama ----------------

    private async Task<string?> TryOllamaAsync(string prompt, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            var payload = JsonSerializer.Serialize(new
            {
                model = _model,
                prompt,
                stream = false,
                options = new { temperature = 0.4, num_predict = 320 }
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/generate")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            using var resp = await _http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            var text = doc.RootElement.TryGetProperty("response", out var r) ? r.GetString() : null;
            return string.IsNullOrWhiteSpace(text) ? null : text!.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Ollama unavailable, using local AI fallback: {Message}", ex.Message);
            return null;
        }
    }

    private static string BuildSummaryPrompt(IReadOnlyList<WorkEntry> entries, string periodLabel)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a concise work assistant. Write a short professional summary ({periodLabel}) " +
                      "for a timesheet status report. Use 3-5 bullet points grouped by project, then a one-line total. " +
                      "Be specific and do not invent data.\n");
        sb.AppendLine("Work entries:");
        foreach (var e in entries.OrderBy(e => e.Date))
            sb.AppendLine($"- {e.Date:yyyy-MM-dd} | {e.Project ?? "General"} | {e.Category} | {e.Hours}h | {e.Task}");
        return sb.ToString();
    }

    // ---------------- Local fallback ----------------

    private static string LocalSummary(IReadOnlyList<WorkEntry> entries, string periodLabel)
    {
        if (entries.Count == 0)
            return $"No work logged for {periodLabel}. Add entries to generate a summary.";

        var total = entries.Sum(e => e.Hours);
        var billable = entries.Where(e => e.Billable).Sum(e => e.Hours);
        var byProject = entries
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Project) ? "General" : e.Project!)
            .OrderByDescending(g => g.Sum(e => e.Hours));

        var sb = new StringBuilder();
        sb.AppendLine($"Work summary — {periodLabel}");
        sb.AppendLine();
        foreach (var g in byProject)
        {
            var hrs = g.Sum(e => e.Hours);
            var topTasks = g.OrderByDescending(e => e.Hours).Take(3).Select(e => e.Task);
            sb.AppendLine($"• {g.Key} ({hrs:0.#}h): {string.Join("; ", topTasks)}");
        }
        sb.AppendLine();
        var done = entries.Count(e => e.Status == WorkStatus.Done);
        var blocked = entries.Count(e => e.Status == WorkStatus.Blocked);
        sb.Append($"Total {total:0.#}h ({billable:0.#}h billable) across {entries.Count} entries — {done} done");
        if (blocked > 0) sb.Append($", {blocked} blocked");
        sb.Append('.');
        return sb.ToString();
    }

    private static readonly Dictionary<string, string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bug"] = "bug", ["fix"] = "bug", ["error"] = "bug", ["issue"] = "bug",
        ["meeting"] = "meeting", ["call"] = "meeting", ["standup"] = "meeting", ["sync"] = "meeting",
        ["deploy"] = "release", ["release"] = "release", ["ship"] = "release",
        ["review"] = "review", ["pr"] = "review",
        ["test"] = "testing", ["qa"] = "testing",
        ["doc"] = "docs", ["spec"] = "docs", ["readme"] = "docs",
        ["design"] = "design", ["ui"] = "design", ["ux"] = "design",
        ["plan"] = "planning", ["roadmap"] = "planning", ["todo"] = "todo", ["task"] = "todo",
        ["idea"] = "idea", ["research"] = "research"
    };

    private static string LocalLabels(string title, string content)
    {
        var text = $"{title} {content}".ToLowerInvariant();
        var labels = Keywords.Where(k => text.Contains(k.Key)).Select(k => k.Value).Distinct().Take(4).ToList();
        if (labels.Count == 0) labels.Add("note");
        return string.Join(", ", labels);
    }
}
