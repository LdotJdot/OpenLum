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
        if (_messages.Count <= reserveRecent)
            return [];
        return _messages.Take(_messages.Count - reserveRecent).ToList();
    }

    public void CompactWithSummary(int reserveRecent, string summary)
    {
        if (_messages.Count <= reserveRecent)
            return;
        var keep = _messages.TakeLast(reserveRecent).ToList();
        _messages.Clear();
        _messages.Add(new ChatMessage
        {
            Role = MessageRole.System,
            Content = $"[Compacted previous conversation]\n{summary}"
        });
        _messages.AddRange(keep);
    }
}
