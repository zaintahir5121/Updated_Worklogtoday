using System.Text;
using System.Text.Json;
using WorklogToday.Models.Domain;

namespace WorklogToday.Services;

public class AiService : IAiService
{
    private readonly HttpClient _http;
    private readonly ILogger<AiService> _logger;
    private readonly string _provider;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly int _timeoutSeconds;

    private static readonly string[] CategoryNames =
        ["Development", "Meeting", "Support", "Review", "Planning", "Research", "Documentation", "Other"];

    public AiService(HttpClient http, IConfiguration config, ILogger<AiService> logger)
    {
        _http = http;
        _logger = logger;
        _provider = config["Ai:Provider"] ?? "Auto";
        _baseUrl = (config["Ai:OllamaBaseUrl"] ?? "http://localhost:11434").TrimEnd('/');
        _model = config["Ai:OllamaModel"] ?? "llama3.2";
        _timeoutSeconds = int.TryParse(config["Ai:TimeoutSeconds"], out var t) ? t : 6;
    }

    // ── Summarize work week ──────────────────────────────────────────────────

    public async Task<AiResponse> SummarizeWorkAsync(IReadOnlyList<WorkEntry> entries, string periodLabel, CancellationToken ct = default)
    {
        var local = LocalSummary(entries, periodLabel);
        if (_provider.Equals("Local", StringComparison.OrdinalIgnoreCase) || entries.Count == 0)
            return new AiResponse(local, "ai");

        var prompt = BuildSummaryPrompt(entries, periodLabel);
        var ai = await TryAiAsync(prompt, ct);
        return new AiResponse(ai ?? local, "ai");
    }

    // ── Suggest note labels ──────────────────────────────────────────────────

    public async Task<AiResponse> SuggestLabelsAsync(string title, string content, CancellationToken ct = default)
    {
        var local = LocalLabels(title, content);
        if (_provider.Equals("Local", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(content))
            return new AiResponse(local, "ai");

        var prompt = $"Suggest 1-4 short, lowercase topic labels (comma separated, no #) for this note. " +
                     $"Reply with ONLY the labels.\nTitle: {title}\nNote: {content}";
        var ai = await TryAiAsync(prompt, ct);
        if (ai == null) return new AiResponse(local, "ai");
        var cleaned = string.Join(", ", ai.Replace("\n", ",").Split(',')
            .Select(s => s.Trim().Trim('#', '.', '-').ToLowerInvariant())
            .Where(s => s.Length is > 1 and < 24).Distinct().Take(4));
        return new AiResponse(string.IsNullOrWhiteSpace(cleaned) ? local : cleaned, "ai");
    }

    // ── Suggest task category + hours ────────────────────────────────────────

    public async Task<AiTaskSuggestion> SuggestTaskDetailsAsync(string taskDescription, CancellationToken ct = default)
    {
        var (localCat, localHours) = LocalTaskDetails(taskDescription);

        if (_provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
            return new AiTaskSuggestion(localCat, localHours, "ai");

        var catList = string.Join(", ", CategoryNames);
        var prompt = $"Given this work task description, reply ONLY with a JSON object with two fields: " +
                     $"\"category\" (one of: {catList}) and \"hours\" (estimated float like 0.5, 1, 1.5, 2, 3, 4). " +
                     $"Example: {{\"category\":\"Development\",\"hours\":2.0}}\n\nTask: {taskDescription}";

        var ai = await TryAiAsync(prompt, ct);
        if (ai != null)
        {
            try
            {
                // strip any markdown fences
                var json = ai.Trim().TrimStart('`').TrimEnd('`');
                if (json.StartsWith("json", StringComparison.OrdinalIgnoreCase)) json = json[4..];
                using var doc = JsonDocument.Parse(json.Trim());
                var root = doc.RootElement;
                var catName = root.TryGetProperty("category", out var cp) ? cp.GetString() ?? "" : "";
                var hours = root.TryGetProperty("hours", out var hp) ? hp.GetDouble() : localHours;
                var catIdx = Array.FindIndex(CategoryNames, c => c.Equals(catName, StringComparison.OrdinalIgnoreCase));
                return new AiTaskSuggestion(catIdx >= 0 ? catIdx : localCat, Math.Clamp(hours, 0.25, 12), "ai");
            }
            catch { /* fall through to local */ }
        }
        return new AiTaskSuggestion(localCat, localHours, "ai");
    }

    // ── Daily standup ────────────────────────────────────────────────────────

    public async Task<AiResponse> GenerateStandupAsync(IReadOnlyList<WorkEntry> entries, CancellationToken ct = default)
    {
        var local = LocalStandup(entries);
        if (_provider.Equals("Local", StringComparison.OrdinalIgnoreCase) || entries.Count == 0)
            return new AiResponse(local, "ai");

        var yesterday = entries.Where(e => e.Date == DateTime.UtcNow.Date.AddDays(-1)).ToList();
        var today = entries.Where(e => e.Date == DateTime.UtcNow.Date).ToList();
        var blocked = entries.Where(e => e.Status == WorkStatus.Blocked).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Write a concise, professional daily standup from these work entries. " +
                      "Format exactly as:\nYESTERDAY:\n• ...\n\nTODAY:\n• ...\n\nBLOCKERS:\n• ... (or 'None')\n\n" +
                      "Keep each section to 1-4 bullets. Be specific and mention project names.\n");
        if (yesterday.Any()) { sb.AppendLine("Yesterday's entries:"); foreach (var e in yesterday) sb.AppendLine($"- {e.Project ?? "General"}: {e.Task} ({e.Hours}h, {e.Status})"); }
        if (today.Any()) { sb.AppendLine("Today's entries:"); foreach (var e in today) sb.AppendLine($"- {e.Project ?? "General"}: {e.Task} ({e.Hours}h, {e.Status})"); }
        if (blocked.Any()) { sb.AppendLine("Blocked:"); foreach (var e in blocked) sb.AppendLine($"- {e.Task}"); }

        var ai = await TryAiAsync(sb.ToString(), ct);
        return new AiResponse(ai ?? local, "ai");
    }

    // ── Extract action items from note ───────────────────────────────────────

    public async Task<(List<AiExtractedTask> Tasks, string Source)> ExtractTasksAsync(string? title, string content, CancellationToken ct = default)
    {
        var local = LocalExtractTasks(content);

        if (_provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
            return (local, "ai");

        var catList = string.Join(", ", CategoryNames);
        var prompt = $"Extract 2-6 specific, actionable work tasks from this note. " +
                     $"Reply ONLY with a JSON array. Each item: {{\"task\":\"short action (max 80 chars)\",\"category\":\"one of: {catList}\",\"hours\":estimated_float}}. " +
                     $"Example: [{{\"task\":\"Fix login redirect bug\",\"category\":\"Development\",\"hours\":1.5}}]\n\n" +
                     $"Note title: {title ?? ""}\nNote content: {content}";

        var ai = await TryAiAsync(prompt, ct);
        if (ai != null)
        {
            try
            {
                var json = ai.Trim().TrimStart('`').TrimEnd('`');
                if (json.StartsWith("json", StringComparison.OrdinalIgnoreCase)) json = json[4..];
                // find array bounds
                var start = json.IndexOf('[');
                var end = json.LastIndexOf(']');
                if (start >= 0 && end > start) json = json[start..(end + 1)];

                using var doc = JsonDocument.Parse(json.Trim());
                var tasks = new List<AiExtractedTask>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var task = el.TryGetProperty("task", out var tp) ? tp.GetString() ?? "" : "";
                    var catName = el.TryGetProperty("category", out var cp) ? cp.GetString() ?? "" : "";
                    var hours = el.TryGetProperty("hours", out var hp) ? hp.GetDouble() : 1.0;
                    if (string.IsNullOrWhiteSpace(task)) continue;
                    var catIdx = Array.FindIndex(CategoryNames, c => c.Equals(catName, StringComparison.OrdinalIgnoreCase));
                    tasks.Add(new AiExtractedTask(task[..Math.Min(task.Length, 280)], catIdx >= 0 ? catIdx : 0, Math.Clamp(hours, 0.25, 8)));
                }
                if (tasks.Count > 0) return (tasks, "ai");
            }
            catch { /* fall through */ }
        }
        return (local, "ai");
    }

    // ── Internal AI engine ───────────────────────────────────────────────────

    private async Task<string?> TryAiAsync(string prompt, CancellationToken ct)
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
                options = new { temperature = 0.4, num_predict = 400 }
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
            _logger.LogDebug("AI engine unavailable, using built-in intelligence: {Message}", ex.Message);
            return null;
        }
    }

    // ── Local intelligence fallbacks ─────────────────────────────────────────

    private static string BuildSummaryPrompt(IReadOnlyList<WorkEntry> entries, string periodLabel)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Write a short professional summary ({periodLabel}) for a timesheet status report. " +
                      "Use 3-5 bullet points grouped by project, then a one-line total. Be specific.\n");
        sb.AppendLine("Work entries:");
        foreach (var e in entries.OrderBy(e => e.Date))
            sb.AppendLine($"- {e.Date:yyyy-MM-dd} | {e.Project ?? "General"} | {e.Category} | {e.Hours}h | {e.Task}");
        return sb.ToString();
    }

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

    private static string LocalStandup(IReadOnlyList<WorkEntry> entries)
    {
        if (entries.Count == 0)
            return "No recent entries found. Log some tasks to generate a standup.";

        var today = DateTime.UtcNow.Date;
        var yEntries = entries.Where(e => e.Date == today.AddDays(-1)).ToList();
        var tEntries = entries.Where(e => e.Date == today).ToList();
        var blockers = entries.Where(e => e.Status == WorkStatus.Blocked).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("YESTERDAY:");
        if (yEntries.Any())
            foreach (var e in yEntries)
                sb.AppendLine($"• [{e.Project ?? "General"}] {e.Task} ({e.Hours:0.#}h)");
        else
            sb.AppendLine("• No entries logged for yesterday.");

        sb.AppendLine();
        sb.AppendLine("TODAY:");
        if (tEntries.Any())
            foreach (var e in tEntries)
                sb.AppendLine($"• [{e.Project ?? "General"}] {e.Task} ({e.Hours:0.#}h)");
        else
            sb.AppendLine("• No tasks logged for today yet.");

        sb.AppendLine();
        sb.AppendLine("BLOCKERS:");
        if (blockers.Any())
            foreach (var e in blockers)
                sb.AppendLine($"• {e.Task} — {e.Project ?? "General"}");
        else
            sb.AppendLine("• None.");

        return sb.ToString().Trim();
    }

    private static List<AiExtractedTask> LocalExtractTasks(string content)
    {
        var tasks = new List<AiExtractedTask>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines.Take(6))
        {
            var clean = line.TrimStart('-', '*', '•', '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '.', ' ');
            if (clean.Length < 8) continue;
            var catIdx = InferCategory(clean);
            tasks.Add(new AiExtractedTask(clean[..Math.Min(clean.Length, 280)], catIdx, 1.0));
        }
        return tasks.Count > 0 ? tasks : [new AiExtractedTask(content[..Math.Min(content.Length, 280)], 0, 1.0)];
    }

    private static (int category, double hours) LocalTaskDetails(string task)
    {
        var cat = InferCategory(task);
        double hours = cat switch
        {
            1 => 0.5,  // meeting
            3 => 1.0,  // review
            4 => 1.0,  // planning
            5 => 2.0,  // research
            6 => 1.5,  // docs
            _ => 2.0
        };
        return (cat, hours);
    }

    private static int InferCategory(string text)
    {
        text = text.ToLowerInvariant();
        if (text.ContainsAny("meet", "standup", "call", "sync", "discuss")) return 1;
        if (text.ContainsAny("support", "ticket", "customer", "help")) return 2;
        if (text.ContainsAny("review", "pr ", "pull request", "code review")) return 3;
        if (text.ContainsAny("plan", "sprint", "roadmap", "backlog", "estimate")) return 4;
        if (text.ContainsAny("research", "spike", "investigate", "explore")) return 5;
        if (text.ContainsAny("doc", "readme", "spec", "wiki", "write up")) return 6;
        if (text.ContainsAny("bug", "fix", "deploy", "build", "implement", "develop", "refactor", "test")) return 0;
        return 7;
    }

    private static readonly Dictionary<string, string> LabelKeywords = new(StringComparer.OrdinalIgnoreCase)
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
        var labels = LabelKeywords.Where(k => text.Contains(k.Key)).Select(k => k.Value).Distinct().Take(4).ToList();
        if (labels.Count == 0) labels.Add("note");
        return string.Join(", ", labels);
    }
}

file static class StringExtensions
{
    public static bool ContainsAny(this string s, params string[] terms) =>
        terms.Any(s.Contains);
}
