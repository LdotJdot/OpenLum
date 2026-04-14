using OpenLum.Console.Interfaces;
using OpenLum.Console.Models;

namespace OpenLum.Console.Compaction;

/// <summary>
/// Compacts session history by summarizing older messages when threshold is exceeded.
/// Optionally collapses failed tool-attempt runs into one short note before summarizing to save tokens.
/// </summary>
public sealed class SessionCompactor
{
    private readonly IModelProvider _model;
    private readonly int _maxMessagesBeforeCompact;
    private readonly int _reserveRecent;
    private readonly bool _collapseFailedAttempts;
    private readonly Action<string>? _onCompactionSummary;

    public SessionCompactor(
        IModelProvider model,
        int maxMessagesBeforeCompact = 30,
        int reserveRecent = 10,
        bool collapseFailedAttempts = true,
        Action<string>? onCompactionSummary = null)
    {
        _model = model;
        _maxMessagesBeforeCompact = maxMessagesBeforeCompact;
        _reserveRecent = reserveRecent;
        _collapseFailedAttempts = collapseFailedAttempts;
        _onCompactionSummary = onCompactionSummary;
    }

    /// <summary>
    /// If session exceeds threshold, summarizes older messages and compacts.
    /// When collapseFailedAttempts is true, replaces runs of [Assistant→Tool(errors)→Assistant] with one short note before summarizing.
    /// Returns true if compaction was performed.
    /// </summary>
    public async Task<bool> CompactIfNeededAsync(
        ICompactableSession session,
        CancellationToken ct = default)
    {
        if (session.MessageCount <= _maxMessagesBeforeCompact)
            return false;

        var toSummarize = session.GetMessagesToCompact(_reserveRecent);
        if (toSummarize.Count == 0)
            return false;

        if (_collapseFailedAttempts)
            toSummarize = CollapseFailedAttempts(toSummarize);

        var summary = await SummarizeAsync(toSummarize, ct);
        session.CompactWithSummary(_reserveRecent, summary);
        _onCompactionSummary?.Invoke(summary);
        return true;
    }

    /// <summary>
    /// Replaces runs of [Assistant with tool_calls] [Tool* with at least one IsToolError] [Assistant] with a single User note to save tokens.
    /// </summary>
    private static List<ChatMessage> CollapseFailedAttempts(IReadOnlyList<ChatMessage> messages)
    {
        var outList = new List<ChatMessage>();
        var i = 0;
        while (i < messages.Count)
        {
            var m = messages[i];
            if (m.Role == MessageRole.Assistant && m.ToolCalls is { Count: > 0 })
            {
                var j = i + 1;
                while (j < messages.Count && messages[j].Role == MessageRole.Tool)
                    j++;
                var toolCount = j - i - 1;
                var hasError = false;
                for (var k = i + 1; k < j; k++)
                {
                    if (messages[k].IsToolError) { hasError = true; break; }
                }
                if (hasError && toolCount > 0 && j < messages.Count && messages[j].Role == MessageRole.Assistant)
                {
                    outList.Add(new ChatMessage { Role = MessageRole.User, Content = "[Previous tool attempt(s) failed; assistant retried.]" });
                    i = j;
                    continue;
                }
            }
            outList.Add(m);
            i++;
        }
        return outList;
    }

    private async Task<string> SummarizeAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var text = FormatForSummarization(messages);
        var prompt = $"""
            Summarize the following conversation concisely. Preserve key decisions, facts, and open questions.
            Output only the summary, no preamble.

            ---

            {text}
            """;

        var response = await _model.ChatAsync(
            [new ChatMessage { Role = MessageRole.User, Content = prompt }],
            [],
            null,
            ct);

        return response.Content.Trim();
    }

    private static string FormatForSummarization(IReadOnlyList<ChatMessage> messages)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var m in messages)
        {
            var role = m.Role switch
            {
                MessageRole.User => "User",
                MessageRole.Assistant => "Assistant",
                MessageRole.Tool => "Tool",
                _ => "System"
            };
            var content = m.Content ?? "";
            if (content.Length > 2000)
                content = content[..2000] + "... [truncated]";
            sb.AppendLine($"[{role}] {content}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
