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
        "Create or overwrite a file with the full `content`. Use for new files or deliberate full rewrites. " +
        "For partial edits to existing files, prefer str_replace or text_edit (replace_range) to avoid accidental full-file mistakes. " +
        "Parent directories are created if missing.";

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
            var lines = 1;
            foreach (var c in content)
            {
                if (c == '\n') lines++;
            }

            return Task.FromResult(
                content.Length == 0
                    ? $"Wrote empty file {pathArg}."
                    : $"Wrote {pathArg} ({content.Length} chars, ~{lines} lines).");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }
}
