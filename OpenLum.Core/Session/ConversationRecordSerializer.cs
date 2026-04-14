using System.Text;
using System.Text.Json;
using OpenLum.Console.Models;
using OpenLum.Console.Tools;

namespace OpenLum.Console.Session;

/// <summary>
/// Persistent conversation snapshot (JSON). Extension: .openlum
/// </summary>
public static class ConversationRecordSerializer
{
    public const string FormatId = "openlum-conversation";
    public const int CurrentVersion = 1;
    private const long MaxFileBytes = 80 * 1024 * 1024;

    public static void Save(
        string path,
        string workspaceDir,
        string modelLabel,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<TodoItem>? todos,
        string? plan)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var dto = new ConversationRecordFileDto
        {
            Format = FormatId,
            Version = CurrentVersion,
            SavedAt = DateTimeOffset.Now.ToString("O"),
            Workspace = workspaceDir,
            ModelLabel = modelLabel,
            Messages = messages.Select(ToDto).ToList(),
            Todos = todos is { Count: > 0 }
                ? todos.Select(t => new TodoItemFileDto { Id = t.Id, Content = t.Content, Status = t.Status }).ToList()
                : null,
            Plan = string.IsNullOrWhiteSpace(plan) ? null : plan
        };

        var json = JsonSerializer.Serialize(dto, ConversationRecordJsonContext.Default.ConversationRecordFileDto);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmp, path, overwrite: true);
    }

    public static ConversationLoadResult Load(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException("Conversation file not found.", full);

        var info = new FileInfo(full);
        if (info.Length > MaxFileBytes)
            throw new InvalidOperationException($"Conversation file too large ({info.Length} bytes).");

        var json = File.ReadAllText(full, Encoding.UTF8);
        var dto = JsonSerializer.Deserialize(json, ConversationRecordJsonContext.Default.ConversationRecordFileDto);
        if (dto is null)
            throw new InvalidOperationException("Invalid conversation file (empty JSON).");
        if (!string.Equals(dto.Format, FormatId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unknown format: {dto.Format}");
        if (dto.Version != CurrentVersion)
            throw new InvalidOperationException($"Unsupported version: {dto.Version}");

        var messages = (dto.Messages ?? []).Select(FromDto).ToList();
        var todos = (dto.Todos ?? []).Select(t => new TodoItem(t.Id, t.Content, t.Status)).ToList();

        return new ConversationLoadResult(
            Workspace: string.IsNullOrWhiteSpace(dto.Workspace) ? null : dto.Workspace.Trim(),
            ModelLabel: dto.ModelLabel?.Trim(),
            Messages: messages,
            Todos: todos,
            Plan: string.IsNullOrWhiteSpace(dto.Plan) ? null : dto.Plan);
    }

    private static MessageFileDto ToDto(ChatMessage m)
    {
        var d = new MessageFileDto
        {
            Role = m.Role.ToString().ToLowerInvariant(),
            Content = m.Content ?? "",
            ToolCallId = m.ToolCallId,
            IsToolError = m.Role == MessageRole.Tool ? m.IsToolError : null
        };
        if (m.ToolCalls is { Count: > 0 } tc)
        {
            d.ToolCalls = tc.Select(t => new ToolCallFileDto
            {
                Id = t.Id,
                Name = t.Name,
                Arguments = t.Arguments ?? "{}"
            }).ToList();
        }

        return d;
    }

    private static ChatMessage FromDto(MessageFileDto d)
    {
        var role = ParseRole(d.Role);
        var toolCalls = d.ToolCalls?.Select(t => new ToolCall
        {
            Id = t.Id ?? "",
            Name = t.Name ?? "",
            Arguments = t.Arguments ?? "{}"
        }).ToList();

        return new ChatMessage
        {
            Role = role,
            Content = d.Content ?? "",
            ToolCalls = toolCalls,
            ToolCallId = d.ToolCallId,
            IsToolError = d.IsToolError ?? false
        };
    }

    private static MessageRole ParseRole(string? s)
    {
        var r = (s ?? "").Trim().ToLowerInvariant();
        return r switch
        {
            "system" => MessageRole.System,
            "user" => MessageRole.User,
            "assistant" => MessageRole.Assistant,
            "tool" => MessageRole.Tool,
            _ => MessageRole.User
        };
    }
}

/// <summary>Result of loading a .openlum conversation file.</summary>
public sealed record ConversationLoadResult(
    string? Workspace,
    string? ModelLabel,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<TodoItem> Todos,
    string? Plan);
