using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Tools;

/// <summary>
/// Writes content to a file in the workspace.
/// </summary>
public sealed class WriteTool : ITool
{
    private readonly string _workspaceDir;

    public WriteTool(string workspaceDir)
    {
        _workspaceDir = Path.GetFullPath(workspaceDir);
    }

    public string Name => "write";
    public string Description => "Create or overwrite a file. Path is relative to workspace.";
    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("path", "string", "Relative path to the file", true),
        new ToolParameter("content", "string", "Content to write", true)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var path = args.GetValueOrDefault("path")?.ToString();
        var content = args.GetValueOrDefault("content")?.ToString();
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult("Error: path is required.");
        if (content is null)
            return Task.FromResult("Error: content is required.");

        var fullPath = Path.GetFullPath(Path.Combine(_workspaceDir, path.TrimStart('/', '\\')));
        if (!fullPath.StartsWith(_workspaceDir, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult("Error: path must be inside workspace.");

        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, content);
            return Task.FromResult($"Wrote {path} ({content.Length} chars).");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }
}
