namespace OpenLum.Console.Session;

/// <summary>JSON root for .openlum (schema version 1).</summary>
public sealed class ConversationRecordFileDto
{
    public string Format { get; set; } = "";
    public int Version { get; set; }
    public string? SavedAt { get; set; }
    public string? Workspace { get; set; }
    public string? ModelLabel { get; set; }
    public List<MessageFileDto>? Messages { get; set; }
    public List<TodoItemFileDto>? Todos { get; set; }
    public string? Plan { get; set; }
}

public sealed class MessageFileDto
{
    public string? Role { get; set; }
    public string? Content { get; set; }
    public List<ToolCallFileDto>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
    public bool? IsToolError { get; set; }
}

public sealed class ToolCallFileDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Arguments { get; set; }
}

public sealed class TodoItemFileDto
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public string Status { get; set; } = "";
}
