using OpenLum.Console.Models;

namespace OpenLum.Console.Interfaces;

/// <summary>
/// Manages conversation history for an agent run.
/// </summary>
public interface ISession
{
    IReadOnlyList<ChatMessage> Messages { get; }
    void Add(ChatMessage message);
    void AddAssistant(string content, IReadOnlyList<ToolCall>? toolCalls = null);
    void AddToolResults(IReadOnlyList<ToolResult> results);
    void Clear();
}
