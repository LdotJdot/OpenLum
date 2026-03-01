using OpenLum.Console.Models;

namespace OpenLum.Console.Interfaces;

/// <summary>
/// JSON schema description for a tool parameter.
/// </summary>
public sealed record ToolParameter(string Name, string Type, string? Description, bool Required = false);

/// <summary>
/// Definition of a tool exposed to the model.
/// </summary>
public sealed record ToolDefinition(
    string Name,
    string Description,
    IReadOnlyList<ToolParameter> Parameters);

/// <summary>
/// Contract for executable tools.
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    IReadOnlyList<ToolParameter> Parameters { get; }
    Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default);
}
