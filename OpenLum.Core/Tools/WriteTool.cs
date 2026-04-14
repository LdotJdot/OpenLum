using OpenLum.Console.Interfaces;
using OpenLum.Console.IO;

namespace OpenLum.Console.Tools;

/// <summary>
/// Creates or overwrites a file.
/// </summary>
public sealed class WriteTool : ITool
{
    private readonly WorkspacePathResolver _resolver;

    public WriteTool(WorkspacePathResolver resolver) => _resolver = resolver;

    public WriteTool(string workspaceDir) : this(new WorkspacePathResolver(workspaceDir)) { }

    public string Name => "write";

    public string Description =>
        "Create or overwrite a file. Use for new files or full rewrites. " +
        "For small edits to existing files, prefer str_replace instead.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("path", "string", "File path (workspace-relative or absolute).", true),
        new ToolParameter("content", "string", "Full content to write.", true)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var pathArg = args.GetValueOrDefault("path")?.ToString()?.Trim();
        var content = args.GetValueOrDefault("content")?.ToString();

        var res = _resolver.Resolve(pathArg);
        if (!res.IsOk)
            return Task.FromResult(res.Error!);
        if (content is null)
            return Task.FromResult("Error: content is required.");

        var fullPath = res.FullPath!;

        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, content);
            return Task.FromResult($"Wrote {pathArg} ({content.Length} chars).");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }
}
