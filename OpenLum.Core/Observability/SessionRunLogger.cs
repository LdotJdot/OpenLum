using System.Text;
using OpenLum.Console.Models;

namespace OpenLum.Console.Observability;

/// <summary>
/// Append-only session log under workspace/.openlum/logs/{yyyy-MM-dd}/session-{timestamp}-{pid}.log
/// </summary>
public sealed class SessionRunLogger : IDisposable
{
    public const int DefaultMaxToolBodyChars = 500_000;

    private readonly object _lock = new();
    private readonly StreamWriter? _writer;
    private readonly bool _ownsWriter;
    private readonly string _linePrefix;

    private SessionRunLogger(StreamWriter? writer, bool ownsWriter, string linePrefix)
    {
        _writer = writer;
        _ownsWriter = ownsWriter;
        _linePrefix = linePrefix;
    }

    /// <summary>
    /// Creates the log file unless disabled via OPENLUM_DISABLE_SESSION_LOG=1 or creation fails.
    /// </summary>
    public static SessionRunLogger? TryCreate(string workspaceDir, string modelLabel, out string? logFilePath)
    {
        logFilePath = null;
        var disable = Environment.GetEnvironmentVariable("OPENLUM_DISABLE_SESSION_LOG");
        if (!string.IsNullOrWhiteSpace(disable) &&
            (disable.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             disable.Equals("true", StringComparison.OrdinalIgnoreCase)))
            return null;

        try
        {
            var day = DateTime.Now.ToString("yyyy-MM-dd");
            var dir = Path.Combine(workspaceDir, ".openlum", "logs", day);
            Directory.CreateDirectory(dir);
            var name = $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}.log";
            var path = Path.Combine(dir, name);
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            logFilePath = path;
            var logger = new SessionRunLogger(writer, true, "");
            logger.WriteHeader(workspaceDir, modelLabel);
            return logger;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Share the same file with a scope prefix.</summary>
    public SessionRunLogger CreateScoped(string scope)
    {
        var p = string.IsNullOrEmpty(_linePrefix) ? $"[{scope}] " : _linePrefix + $"[{scope}] ";
        return new SessionRunLogger(_writer, false, p);
    }

    private void WriteHeader(string workspaceDir, string modelLabel)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== OpenLum session log ===");
        sb.AppendLine($"Started (local): {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"PID: {Environment.ProcessId}");
        sb.AppendLine($"Workspace: {workspaceDir}");
        sb.AppendLine($"Model: {modelLabel}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine();
        WriteRaw(sb.ToString());
    }

    public void WriteRaw(string text)
    {
        if (_writer is null) return;
        lock (_lock)
        {
            _writer.Write(text);
        }
    }

    public void WriteLine(string message)
    {
        if (_writer is null) return;
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        lock (_lock)
        {
            _writer.WriteLine($"[{ts}] {_linePrefix}{message}");
        }
    }

    public void WriteSection(string title, string body, int maxChars = DefaultMaxToolBodyChars)
    {
        if (_writer is null) return;
        var trimmed = body ?? "";
        string payload;
        if (trimmed.Length > maxChars)
            payload = trimmed[..maxChars] + $"\n... [{trimmed.Length - maxChars} more characters truncated for log]";
        else
            payload = trimmed;

        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        lock (_lock)
        {
            _writer.WriteLine($"[{ts}] {_linePrefix}==== {title} ====");
            foreach (var line in payload.Split('\n'))
                _writer.WriteLine($"{_linePrefix}{line}");
            _writer.WriteLine($"[{ts}] {_linePrefix}==== end {title} ====");
        }
    }

    public void WriteUserMessage(string text) =>
        WriteSection("user_message", text, maxChars: 2_000_000);

    public void WriteModelRound(int turn, string phaseLabel, int messageCount, int approxPromptChars)
    {
        var promptEst = TokenEstimator.RoughTokensFromChars(approxPromptChars);
        WriteLine(
            $"model_turn={turn} phase={phaseLabel} history_messages={messageCount} approx_prompt_chars={approxPromptChars} (~{promptEst} tokens est, message text only; tool schema not counted)");
    }

    public void WriteAssistantResponse(string? content, IReadOnlyList<ToolCall> toolCalls, ModelTokenUsage? usage)
    {
        var tc = toolCalls.Count == 0
            ? "(none)"
            : string.Join("; ", toolCalls.Select(t => $"{t.Name}({t.Id})"));
        WriteLine($"assistant_tool_calls: {tc}");
        if (usage is { } u)
        {
            WriteLine(
                $"token_usage: prompt={u.PromptTokens?.ToString() ?? "?"}, completion={u.CompletionTokens?.ToString() ?? "?"}, total={u.TotalTokens?.ToString() ?? "?"}");
        }
        else
        {
            var c = content ?? "";
            WriteLine(
                $"token_usage: (not reported by API) completion_chars={c.Length} (~{TokenEstimator.RoughTokensFromText(c)} tokens est from text length)");
        }

        WriteSection("assistant_content", content ?? "", maxChars: 2_000_000);
    }

    public void WriteToolStarting(string toolName, string? argsJson) =>
        WriteLine($"tool_start name={toolName} args={Truncate(argsJson, 8000)}");

    public void WriteToolCompleted(string toolName, string? argsJson, bool success, string resultBody, int maxResultChars = DefaultMaxToolBodyChars)
    {
        WriteLine($"tool_end name={toolName} success={success} args={Truncate(argsJson, 2000)}");
        WriteSection($"tool_result:{toolName}", resultBody, maxResultChars);
    }

    public void WriteCompaction(string summary) =>
        WriteSection("compaction_summary", summary, maxChars: 200_000);

    /// <summary>
    /// Marks a new logical session in the same log file.
    /// </summary>
    public void WriteLogicalSessionBoundary(string reason)
    {
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        WriteRaw(
            $"\n\n========== NEW LOGICAL SESSION ({ts}) ==========\n" +
            $"Reason: {reason}\n" +
            "Previous chat history was cleared on the host; following messages are a fresh session.\n" +
            "==================================================\n\n");
    }

    public void WriteError(string context, Exception? ex)
    {
        WriteLine($"ERROR {context}: {ex?.Message ?? "unknown"}");
        if (ex?.StackTrace is { } st)
            WriteSection("error_stack", st, 50_000);
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace('\r', ' ').Replace('\n', ' ');
        return s.Length <= max ? s : s[..max] + "...";
    }

    public void Dispose()
    {
        if (_ownsWriter && _writer is not null)
        {
            lock (_lock)
            {
                _writer.Dispose();
            }
        }
    }
}
