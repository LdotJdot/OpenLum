using OpenLum.Console.Interfaces;
using OpenLum.Console.Models;

namespace OpenLum.Console.Compaction;

/// <summary>
/// Compacts session history by summarizing older messages when threshold is exceeded.
/// </summary>
public sealed class SessionCompactor
{
    private readonly IModelProvider _model;
    private readonly int _maxMessagesBeforeCompact;
    private readonly int _reserveRecent;

    public SessionCompactor(
        IModelProvider model,
        int maxMessagesBeforeCompact = 30,
        int reserveRecent = 10)
    {
        _model = model;
        _maxMessagesBeforeCompact = maxMessagesBeforeCompact;
        _reserveRecent = reserveRecent;
    }

    /// <summary>
    /// If session exceeds threshold, summarizes older messages and compacts.
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

        var summary = await SummarizeAsync(toSummarize, ct);
        session.CompactWithSummary(_reserveRecent, summary);
        return true;
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
