using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Tools;

/// <summary>
/// Lists files and directories in PowerShell Get-ChildItem style.
/// </summary>
public sealed class ListDirTool : ITool
{
    private readonly string _workspaceDir;

    public ListDirTool(string workspaceDir)
    {
        _workspaceDir = Path.GetFullPath(workspaceDir);
    }

    public string Name => "list_dir";
    public string Description => "PowerShell-style: like Get-ChildItem. List files and subdirectories. Path: workspace-relative or absolute.";
    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("path", "string", "Path (default: .). Like Get-ChildItem -Path.", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var path = args.GetValueOrDefault("path")?.ToString()?.Trim() ?? ".";
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_workspaceDir, path.TrimStart('/', '\\')));

        if (!Directory.Exists(fullPath))
            return Task.FromResult($"Error: directory not found: {path}");

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
