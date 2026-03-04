using System.Collections.ObjectModel;
using OpenLum.Console.Interfaces;
using OpenLum.Console.Models;

namespace OpenLum.Console.Session;

/// <summary>
/// In-memory session with conversation history. Supports compaction.
/// </summary>
public sealed class ConsoleSession : ICompactableSession
{
    private readonly List<ChatMessage> _messages = [];

    public IReadOnlyList<ChatMessage> Messages => new ReadOnlyCollection<ChatMessage>(_messages);
    public int MessageCount => _messages.Count;

    public void Add(ChatMessage message) => _messages.Add(message);

    public void AddAssistant(string content, IReadOnlyList<ToolCall>? toolCalls = null)
    {
        _messages.Add(new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = content,
            ToolCalls = toolCalls
        });
    }

    public void AddToolResults(IReadOnlyList<ToolResult> results)
    {
        foreach (var r in results)
        {
            _messages.Add(new ChatMessage
            {
                Role = MessageRole.Tool,
                Content = r.Content,
                ToolCallId = r.ToolCallId
            });
        }
    }

    public void Clear() => _messages.Clear();

    public IReadOnlyList<ChatMessage> GetMessagesToCompact(int reserveRecent)
    {
        var compactEnd = GetSafeReserveStartIndex(reserveRecent);
        if (compactEnd <= 0)
            return [];
        return _messages.Take(compactEnd).ToList();
    }

    public void CompactWithSummary(int reserveRecent, string summary)
    {
        var keepStart = GetSafeReserveStartIndex(reserveRecent);
        if (keepStart >= _messages.Count)
            return;
        var keep = _messages.Skip(keepStart).ToList();
        _messages.Clear();
        _messages.Add(new ChatMessage
        {
            Role = MessageRole.System,
            Content = $"[Compacted previous conversation]\n{summary}"
        });
        _messages.AddRange(keep);
    }

    /// <summary>
    /// Computes the start index for messages to keep. Ensures we never cut in the middle
    /// of an assistant+tool block, which would leave orphan Tool messages and cause
    /// "Messages with role 'tool' must be a response to a preceding message with 'tool_calls'" API errors.
    /// </summary>
    private int GetSafeReserveStartIndex(int reserveRecent)
    {
        if (_messages.Count <= reserveRecent)
            return _messages.Count;
        var keepStart = _messages.Count - reserveRecent;
        // If the first kept message is Tool, extend backward to include its owning Assistant
        while (keepStart > 0 && _messages[keepStart].Role == MessageRole.Tool)
            keepStart--;
        return keepStart;
    }
}
