using OpenLum.Console.Models;

namespace OpenLum.Console.Interfaces;

/// <summary>
/// Response from the model including text and optional tool calls.
/// </summary>
public sealed record ModelResponse(
    string Content,
    IReadOnlyList<ToolCall> ToolCalls,
    ModelTokenUsage? Usage = null);

/// <summary>
/// Contract for LLM inference.
/// </summary>
public interface IModelProvider
{
    Task<ModelResponse> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        IProgress<string>? contentProgress,
        CancellationToken ct = default);
}

