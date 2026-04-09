using OpenLum.Console.Interfaces;
using OpenLum.Console.IO;

namespace OpenLum.Console.Tools;

/// <summary>
/// Lists files and directories in PowerShell Get-ChildItem style.
/// </summary>
public sealed class ListDirTool : ITool
{
    private readonly WorkspacePathResolver _resolver;

    public ListDirTool(WorkspacePathResolver resolver) => _resolver = resolver;

    public ListDirTool(string workspaceDir) : this(new WorkspacePathResolver(workspaceDir)) { }

    public string Name => "list_dir";

    public string Description =>
        "List files and subdirectories. Path defaults to workspace root; any absolute path is also accepted.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("path", "string", "Directory path (workspace-relative or absolute; default: workspace root).", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var pathArg = args.GetValueOrDefault("path")?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(pathArg)) pathArg = ".";

        var res = _resolver.ResolveExistingDirectory(pathArg);
        if (!res.IsOk)
            return Task.FromResult(res.Error!);

        var fullPath = res.FullPath!;

        var entries = Directory.GetFileSystemEntries(fullPath)
            .Select(p => new FileSystemInfoWrapper(p))
            .Where(e => !e.Name.StartsWith('.') || e.Name is "." or "..")
            .OrderBy(e => e.IsDirectory ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"    Directory: {fullPath}");
        sb.AppendLine();
        sb.AppendLine("Mode                 LastWriteTime         Length Name");
        sb.AppendLine("----                 -------------         ------ ----");

        foreach (var e in entries)
        {
            var mode = e.IsDirectory ? "d-----" : "-a----";
            var time = e.LastWriteTime.ToString("M/d/yyyy   h:mm tt");
            var len = e.IsDirectory ? "" : e.Length.ToString().PadLeft(16);
            sb.AppendLine($"{mode}        {time} {len}  {e.Name}");
        }

        return Task.FromResult(sb.Length > 0 ? sb.ToString() : "(empty)");
    }

    private sealed class FileSystemInfoWrapper
    {
        public string Name { get; }
        public bool IsDirectory { get; }
        public long Length { get; }
        public DateTime LastWriteTime { get; }

        public FileSystemInfoWrapper(string path)
        {
            Name = Path.GetFileName(path) ?? "";
            IsDirectory = Directory.Exists(path);
            if (IsDirectory)
            {
                Length = 0;
                LastWriteTime = Directory.GetLastWriteTime(path);
            }
            else
            {
                var fi = new FileInfo(path);
                Length = fi.Length;
                LastWriteTime = fi.LastWriteTime;
            }
        }
    }
}
