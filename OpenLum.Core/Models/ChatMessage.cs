namespace OpenLum.Console.Models;

/// <summary>
/// Role of a chat message.
/// </summary>
public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}

/// <summary>
/// A single message in the conversation.
/// Tool calls appear on assistant messages; tool results use Role.Tool.
/// </summary>
public sealed class ChatMessage
{
    public required MessageRole Role { get; init; }
    public string Content { get; init; } = string.Empty;

    /// <summary>Tool calls from an assistant message.</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    /// <summary>Tool call id for a tool result message.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>When Role is Tool: true if the tool returned an error. Used to truncate or collapse failed attempts to save tokens.</summary>
    public bool IsToolError { get; init; }
}

/// <summary>
/// A tool call requested by the model.
/// </summary>
public sealed class ToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Arguments { get; init; }
}

/// <summary>
/// Result of a tool execution.
/// </summary>
public sealed class ToolResult
{
    public required string ToolCallId { get; init; }
    public required string Content { get; init; }
    public bool IsError { get; init; }
}

