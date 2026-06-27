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
    private readonly string? _openAiKey;
    private readonly string? _groqKey;
    private const string GroqChatUrl = "https://api.groq.com/openai/v1/chat/completions";
    private const string GroqWhisperUrl = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const string GroqFastModel = "llama-3.1-8b-instant";
    private const string GroqWhisperModel = "whisper-large-v3";

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
        _openAiKey = config["OpenAI:ApiKey"];
        _groqKey = config["Groq:ApiKey"];
    }

    // ── Summarize work week ──────────────────────────────────────────────────

    public async Task<AiResponse> SummarizeWorkAsync(IReadOnlyList<WorkEntry> entries, string periodLabel, CancellationToken ct = default)
    {
        var local = LocalSummary(entries, periodLabel);
        if (_provider.Equals("Local", StringComparison.OrdinalIgnoreCase) || entries.Count == 0)
            return new AiResponse(local, "ai");

        var prompt = BuildSummaryPrompt(entries, periodLabel);
        var ai = await TryCloudThenOllamaAsync(prompt, 500, ct);
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
        var ai = await TryCloudThenOllamaAsync(prompt, 60, ct);
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

        var ai = await TryCloudThenOllamaAsync(prompt, 80, ct);
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

        var ai = await TryCloudThenOllamaAsync(sb.ToString(), 400, ct);
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

        var ai = await TryCloudThenOllamaAsync(prompt, 500, ct);
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

    // ── Ask AI (Notes Q&A) ───────────────────────────────────────────────────

    public async Task<AiResponse> AskNotesAsync(string question, IReadOnlyList<Note> notes, CancellationToken ct = default)
    {
        var local = LocalAskNotes(question, notes);
        if (_provider.Equals("Local", StringComparison.OrdinalIgnoreCase) || notes.Count == 0)
            return new AiResponse(local, "ai");

        var context = new StringBuilder();
        foreach (var n in notes.Take(20))
        {
            context.AppendLine($"--- Note: {n.Title ?? "(untitled)"} ---");
            context.AppendLine(n.Content.Length > 600 ? n.Content[..600] + "…" : n.Content);
        }

        var prompt = $"You are a personal note assistant. Answer the user's question using ONLY the notes provided below. " +
                     $"Be concise and specific. If the answer is not in the notes, say so clearly.\n\n" +
                     $"Question: {question}\n\nNotes:\n{context}";

        var ai = await TryCloudThenOllamaAsync(prompt, 300, ct);
        return new AiResponse(ai ?? local, "ai");
    }

    // ── Weekly Retrospective ─────────────────────────────────────────────────

    public async Task<AiResponse> GenerateRetroAsync(IReadOnlyList<WorkEntry> entries, CancellationToken ct = default)
    {
        var local = LocalRetro(entries);
        if (_provider.Equals("Local", StringComparison.OrdinalIgnoreCase) || entries.Count == 0)
            return new AiResponse(local, "ai");

        var sb = new StringBuilder();
        sb.AppendLine("Generate a brief weekly retrospective in exactly this format:\n" +
                      "✅ WENT WELL:\n• ...\n\n⚠️ COULD IMPROVE:\n• ...\n\n🎯 NEXT WEEK:\n• ...\n\n" +
                      "Keep each section to 2-3 bullets. Base it on these work entries:\n");
        foreach (var e in entries.OrderBy(e => e.Date))
            sb.AppendLine($"- {e.Date:ddd dd MMM} | {e.Project ?? "General"} | {e.Category} | {e.Hours}h | {e.Status} | {e.Task}");
        var blocked = entries.Count(e => e.Status == WorkStatus.Blocked);
        var done = entries.Count(e => e.Status == WorkStatus.Done);
        sb.AppendLine($"\nStats: {entries.Count} entries, {done} done, {blocked} blocked, {entries.Sum(e => e.Hours):0.#}h total.");

        var ai = await TryCloudThenOllamaAsync(sb.ToString(), 500, ct);
        return new AiResponse(ai ?? local, "ai");
    }

    // ── Productivity Insights ────────────────────────────────────────────────

    public async Task<AiResponse> GenerateProductivityInsightAsync(IReadOnlyList<WorkEntry> entries, CancellationToken ct = default)
    {
        var local = LocalProductivityInsight(entries);
        if (_provider.Equals("Local", StringComparison.OrdinalIgnoreCase) || entries.Count < 3)
            return new AiResponse(local, "ai");

        var byDay = entries.GroupBy(e => e.Date.DayOfWeek)
            .Select(g => $"{g.Key}: {g.Sum(e => e.Hours):0.#}h").ToList();
        var byCat = entries.GroupBy(e => e.Category)
            .OrderByDescending(g => g.Sum(e => e.Hours))
            .Select(g => $"{g.Key}: {g.Sum(e => e.Hours):0.#}h").ToList();
        var total = entries.Sum(e => e.Hours);
        var billable = entries.Where(e => e.Billable).Sum(e => e.Hours);
        var meetings = entries.Where(e => e.Category == WorkCategory.Meeting).Sum(e => e.Hours);
        var blocked = entries.Count(e => e.Status == WorkStatus.Blocked);

        var prompt = $"Analyze this professional's work data and give 3-5 specific, actionable productivity insights. " +
                     $"Focus on time distribution, potential improvements, and positive patterns. " +
                     $"Format as bullet points starting with an emoji. Be encouraging but practical.\n\n" +
                     $"Total hours: {total:0.#}h | Billable: {billable:0.#}h | Blocked tasks: {blocked}\n" +
                     $"Hours by day: {string.Join(", ", byDay)}\n" +
                     $"Hours by category: {string.Join(", ", byCat)}\n" +
                     $"Meeting hours: {meetings:0.#}h ({(total > 0 ? meetings / total * 100 : 0):0}% of total)";

        var ai = await TryCloudThenOllamaAsync(prompt, 400, ct);
        return new AiResponse(ai ?? local, "ai");
    }

    // ── Status Update Generator ──────────────────────────────────────────────

    public async Task<AiResponse> GenerateStatusUpdateAsync(IReadOnlyList<WorkEntry> entries, string format, CancellationToken ct = default)
    {
        var local = LocalStatusUpdate(entries, format);
        if (_provider.Equals("Local", StringComparison.OrdinalIgnoreCase) || entries.Count == 0)
            return new AiResponse(local, "ai");

        var sb = new StringBuilder();
        bool isSlack = format.Equals("slack", StringComparison.OrdinalIgnoreCase);

        if (isSlack)
            sb.AppendLine("Write a concise Slack status update for the week. Use Slack formatting (*bold*, bullet points with •). " +
                          "Start with a one-line summary, then 3-5 bullet highlights grouped by project. End with any blockers. Keep it under 200 words.\n");
        else
            sb.AppendLine("Write a professional email-style weekly status update. Use a subject line, then a brief paragraph summary, " +
                          "then bullet points grouped by project. End with blockers/risks if any. Keep it under 250 words. Professional tone.\n");

        sb.AppendLine("Work entries this week:");
        foreach (var g in entries.GroupBy(e => string.IsNullOrWhiteSpace(e.Project) ? "General" : e.Project!))
        {
            sb.AppendLine($"\n{g.Key} ({g.Sum(e => e.Hours):0.#}h):");
            foreach (var e in g) sb.AppendLine($"  - {e.Task} ({e.Status})");
        }
        sb.AppendLine($"\nTotal: {entries.Sum(e => e.Hours):0.#}h | Blocked: {entries.Count(e => e.Status == WorkStatus.Blocked)}");

        var ai = await TryCloudThenOllamaAsync(sb.ToString(), 400, ct);
        return new AiResponse(ai ?? local, "ai");
    }

    // ── Whisper transcription (Groq → OpenAI → skip) ────────────────────────

    public async Task<string> TranscribeAudioAsync(byte[] audioData, string fileName, CancellationToken ct = default)
    {
        if (audioData.Length == 0) return string.Empty;

        // Groq Whisper (free, fast)
        if (!string.IsNullOrWhiteSpace(_groqKey))
        {
            var result = await WhisperCallAsync(GroqWhisperUrl, _groqKey!, GroqWhisperModel, audioData, fileName, ct);
            if (!string.IsNullOrEmpty(result)) return result;
        }

        // OpenAI Whisper (fallback)
        if (!string.IsNullOrWhiteSpace(_openAiKey))
        {
            var result = await WhisperCallAsync("https://api.openai.com/v1/audio/transcriptions", _openAiKey!, "whisper-1", audioData, fileName, ct);
            if (!string.IsNullOrEmpty(result)) return result;
        }

        return string.Empty;
    }

    private async Task<string> WhisperCallAsync(string url, string key, string model, byte[] audioData, string fileName, CancellationToken ct)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(audioData), "file", fileName);
            content.Add(new StringContent(model), "model");
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts2.CancelAfter(TimeSpan.FromSeconds(60));
            using var resp = await _http.SendAsync(req, cts2.Token);
            if (!resp.IsSuccessStatusCode) return string.Empty;
            var json = await resp.Content.ReadAsStringAsync(cts2.Token);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("text", out var txt) ? txt.GetString()?.Trim() ?? "" : "";
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Whisper call to {Url} failed: {Message}", url, ex.Message);
            return string.Empty;
        }
    }

    // ── Meeting notes generation ─────────────────────────────────────────────

    public async Task<AiResponse> GenerateMeetingNotesAsync(string transcript, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return new AiResponse("No transcript available — please try again.", "local");

        var prompt = BuildMeetingNotesPrompt(transcript);
        var result = await TryCloudThenOllamaAsync(prompt, 900, ct);
        return new AiResponse(result ?? LocalMeetingNotes(transcript), result != null ? "ai" : "local");
    }

    // ── Unified fast chat: Groq → OpenAI → Ollama ───────────────────────────

    private async Task<string?> TryCloudThenOllamaAsync(string prompt, int maxTokens, CancellationToken ct)
    {
        // 1. Groq (fastest — free)
        if (!string.IsNullOrWhiteSpace(_groqKey))
        {
            var r = await OpenAiCompatChatAsync(GroqChatUrl, _groqKey!, GroqFastModel, prompt, maxTokens, ct);
            if (r != null) return r;
        }

        // 2. OpenAI GPT-4o-mini
        if (!string.IsNullOrWhiteSpace(_openAiKey))
        {
            var r = await OpenAiCompatChatAsync("https://api.openai.com/v1/chat/completions", _openAiKey!, "gpt-4o-mini", prompt, maxTokens, ct);
            if (r != null) return r;
        }

        // 3. Local Ollama
        return await TryCloudThenOllamaAsync(prompt, 900, ct);
    }

    private async Task<string?> OpenAiCompatChatAsync(string url, string key, string model, string prompt, int maxTokens, CancellationToken ct)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                model,
                messages = new[] { new { role = "user", content = prompt } },
                max_tokens = maxTokens,
                temperature = 0.3
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts2.CancelAfter(TimeSpan.FromSeconds(20));
            using var resp = await _http.SendAsync(req, cts2.Token);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(cts2.Token);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Chat call to {Url} failed: {Message}", url, ex.Message);
            return null;
        }
    }

    private static string BuildMeetingNotesPrompt(string transcript) =>
        "Generate structured meeting notes from this transcript. Use exactly this format:\n\n" +
        "## Meeting Summary\n(2-3 sentence overview of what was discussed)\n\n" +
        "## Key Decisions\n• Decision 1\n• Decision 2\n\n" +
        "## Action Items\n• [Owner if mentioned] Action description\n\n" +
        "## Next Steps\n• Next step 1\n\n" +
        "Be specific and concise. Only include what is explicitly in the transcript. " +
        "If a section has nothing, write '• None identified'.\n\n" +
        $"Transcript:\n{transcript}";

    private static string LocalMeetingNotes(string transcript)
    {
        var lines = transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var preview = transcript.Length > 300 ? transcript[..300] + "…" : transcript;
        var sb = new StringBuilder();
        sb.AppendLine("## Meeting Summary");
        sb.AppendLine(preview);
        sb.AppendLine();
        sb.AppendLine("## Key Decisions");
        sb.AppendLine("• Review the full transcript below for decisions.");
        sb.AppendLine();
        sb.AppendLine("## Action Items");
        sb.AppendLine("• Review the full transcript below for action items.");
        sb.AppendLine();
        sb.AppendLine("## Next Steps");
        sb.AppendLine("• Follow up with meeting participants.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("## Full Transcript");
        sb.AppendLine(transcript);
        return sb.ToString().Trim();
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

    private static string LocalAskNotes(string question, IReadOnlyList<Note> notes)
    {
        if (notes.Count == 0) return "You have no notes yet. Create some notes to use the Q&A feature.";
        var q = question.ToLowerInvariant();
        var relevant = notes
            .Select(n => new { n, score = ScoreNoteRelevance(q, n) })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(3)
            .Select(x => x.n)
            .ToList();
        if (relevant.Count == 0)
            return $"No notes found matching \"{question}\". Try a different keyword or create notes on this topic.";
        var sb = new StringBuilder();
        sb.AppendLine($"Based on your notes, here's what I found for \"{question}\":\n");
        foreach (var n in relevant)
        {
            sb.AppendLine($"📌 {n.Title ?? "(untitled)"}");
            sb.AppendLine(n.Content.Length > 200 ? n.Content[..200] + "…" : n.Content);
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }

    private static int ScoreNoteRelevance(string query, Note note)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var text = $"{note.Title} {note.Content} {note.Labels}".ToLowerInvariant();
        return words.Count(w => w.Length > 2 && text.Contains(w));
    }

    private static string LocalRetro(IReadOnlyList<WorkEntry> entries)
    {
        if (entries.Count == 0)
            return "No entries found for this period. Log work to generate a retrospective.";

        var done = entries.Where(e => e.Status == WorkStatus.Done).ToList();
        var blocked = entries.Where(e => e.Status == WorkStatus.Blocked).ToList();
        var total = entries.Sum(e => e.Hours);
        var topProjects = entries.GroupBy(e => e.Project ?? "General").OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key);

        var sb = new StringBuilder();
        sb.AppendLine("✅ WENT WELL:");
        if (done.Any())
            foreach (var e in done.Take(3)) sb.AppendLine($"• Completed: {e.Task} ({e.Project ?? "General"})");
        else
            sb.AppendLine($"• Logged {total:0.#}h across {entries.Count} tasks — good effort tracking!");
        sb.AppendLine();
        sb.AppendLine("⚠️ COULD IMPROVE:");
        if (blocked.Any())
            foreach (var e in blocked.Take(2)) sb.AppendLine($"• Unblocked needed: {e.Task}");
        else
            sb.AppendLine("• Consider tagging tasks as Done/Blocked for better tracking.");
        if (total > 0 && entries.Count(e => e.Category == WorkCategory.Meeting) > 3)
            sb.AppendLine("• High meeting load detected — consider blocking focus time.");
        sb.AppendLine();
        sb.AppendLine("🎯 NEXT WEEK:");
        sb.AppendLine($"• Follow up on active projects: {string.Join(", ", topProjects)}");
        sb.AppendLine("• Review any blocked items and clear dependencies.");
        if (blocked.Any())
            sb.AppendLine($"• Resolve {blocked.Count} blocked task(s) from this week.");
        return sb.ToString().Trim();
    }

    private static string LocalProductivityInsight(IReadOnlyList<WorkEntry> entries)
    {
        if (entries.Count < 3)
            return "Log more tasks (at least 3) to get productivity insights.";

        var total = entries.Sum(e => e.Hours);
        var billable = entries.Where(e => e.Billable).Sum(e => e.Hours);
        var meetings = entries.Where(e => e.Category == WorkCategory.Meeting).Sum(e => e.Hours);
        var devHours = entries.Where(e => e.Category == WorkCategory.Development).Sum(e => e.Hours);
        var blocked = entries.Count(e => e.Status == WorkStatus.Blocked);
        var busiest = entries.GroupBy(e => e.Date.DayOfWeek).OrderByDescending(g => g.Sum(e => e.Hours)).FirstOrDefault();

        var sb = new StringBuilder();
        sb.AppendLine("📊 Your Productivity Insights:\n");

        var meetPct = total > 0 ? meetings / total * 100 : 0;
        if (meetPct > 35)
            sb.AppendLine($"⚠️ **Meeting-heavy week** — {meetPct:0}% of time in meetings. Try blocking 2-hour focus sessions.");
        else if (meetPct < 15)
            sb.AppendLine($"✅ **Good focus ratio** — only {meetPct:0}% in meetings, leaving plenty of deep work time.");

        if (billable > 0 && total > 0)
        {
            var billPct = billable / total * 100;
            sb.AppendLine(billPct >= 70
                ? $"💰 **Strong billable ratio** — {billPct:0}% of hours are billable. Great client focus."
                : $"💡 **Billable opportunity** — only {billPct:0}% billable. Review which tasks can be billed.");
        }

        if (busiest != null)
            sb.AppendLine($"📅 **Peak day: {busiest.Key}** — most productive day with {busiest.Sum(e => e.Hours):0.#}h logged.");

        if (blocked > 0)
            sb.AppendLine($"🚧 **{blocked} blocked task(s)** — unblock these first to maintain momentum.");

        var projectCount = entries.Select(e => e.Project).Distinct().Count();
        if (projectCount > 5)
            sb.AppendLine($"🔄 **Context switching** — working across {projectCount} projects. Consider batching similar work.");
        else
            sb.AppendLine($"🎯 **Focused** — working on {projectCount} project(s). Good context discipline.");

        return sb.ToString().Trim();
    }

    private static string LocalStatusUpdate(IReadOnlyList<WorkEntry> entries, string format)
    {
        if (entries.Count == 0) return "No entries found for this period.";

        var total = entries.Sum(e => e.Hours);
        var done = entries.Count(e => e.Status == WorkStatus.Done);
        var blocked = entries.Where(e => e.Status == WorkStatus.Blocked).ToList();
        var byProject = entries.GroupBy(e => string.IsNullOrWhiteSpace(e.Project) ? "General" : e.Project!).OrderByDescending(g => g.Sum(e => e.Hours));

        bool isSlack = format.Equals("slack", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();

        if (isSlack)
        {
            sb.AppendLine($"*Weekly Update* — {total:0.#}h logged, {done} tasks completed");
            sb.AppendLine();
            foreach (var g in byProject)
            {
                sb.AppendLine($"*{g.Key}* ({g.Sum(e => e.Hours):0.#}h)");
                foreach (var e in g.Take(3)) sb.AppendLine($"• {e.Task}");
            }
            if (blocked.Any()) { sb.AppendLine(); sb.AppendLine("*Blockers:*"); foreach (var e in blocked) sb.AppendLine($"• {e.Task}"); }
        }
        else
        {
            sb.AppendLine($"Subject: Weekly Status Update — {DateTime.UtcNow:MMMM dd, yyyy}");
            sb.AppendLine();
            sb.AppendLine($"This week I logged {total:0.#} hours across {entries.Count} tasks, completing {done} items.");
            sb.AppendLine();
            sb.AppendLine("Highlights by project:");
            foreach (var g in byProject)
            {
                sb.AppendLine($"\n{g.Key} ({g.Sum(e => e.Hours):0.#}h):");
                foreach (var e in g.Take(3)) sb.AppendLine($"  • {e.Task} [{e.Status}]");
            }
            if (blocked.Any())
            {
                sb.AppendLine("\nBlockers / Risks:");
                foreach (var e in blocked) sb.AppendLine($"  • {e.Task}");
            }
            sb.AppendLine("\nBest regards");
        }
        return sb.ToString().Trim();
    }

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
