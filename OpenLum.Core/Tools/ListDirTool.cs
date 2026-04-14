using System.IO.Enumeration;
using OpenLum.Console.Interfaces;
using OpenLum.Console.IO;
using static OpenLum.Console.Tools.ToolArgHelpers;

namespace OpenLum.Console.Tools;

/// <summary>
/// Lists files and directories: single-level table (default) or recursive flat full paths with truncation.
/// </summary>
public sealed class ListDirTool : ITool
{
    private const int DefaultMaxLines = 800;
    private const int AbsoluteMaxLines = 10_000;

    private readonly WorkspacePathResolver _resolver;

    public ListDirTool(WorkspacePathResolver resolver) => _resolver = resolver;

    public ListDirTool(string workspaceDir) : this(new WorkspacePathResolver(workspaceDir)) { }

    public string Name => "list_dir";

    public string Description =>
        "List directory contents. Default: one level, table layout. " +
        "Optional name_glob when recursive is false: filter entry names (glob * ?). " +
        "If the user already gave a resolvable file path, prefer read over listing the parent. " +
        "Recursive listing can be very large; when the user only needs files matching a name/extension pattern under a known folder, prefer a targeted discovery pass over walking the full tree first. " +
        "Set recursive=true for a flat list of full paths (depth-first, sorted per directory)—like a tree flattened to lines, not indented tree text. " +
        "Use max_lines (default 800, cap 10000) to truncate huge trees; if truncated, call again with path=<a subdirectory> or lower max_depth. " +
        "max_depth 0 = unlimited depth; max_depth N limits how many levels below the given path are expanded.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("path", "string", "Directory path (workspace-relative or absolute; default: workspace root).", false),
        new ToolParameter("recursive", "boolean", "If true, list all files and dirs under path as full paths, one per line.", false),
        new ToolParameter("max_lines", "number", "Max lines when recursive (default 800, max 10000).", false),
        new ToolParameter("max_depth", "number", "When recursive: 0 = unlimited depth; N = only descend N levels below path.", false),
        new ToolParameter("full_paths", "boolean", "When recursive is false: if true, print one full path per line (single level only) instead of the table.", false),
        new ToolParameter("name_glob", "string", "When recursive is false: optional glob to filter entry names (* and ?). Ignored when recursive=true.", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var pathArg = ParseString(args, "path");
        if (string.IsNullOrWhiteSpace(pathArg)) pathArg = ".";

        var res = _resolver.ResolveExistingDirectory(pathArg);
        if (!res.IsOk)
            return Task.FromResult(res.Error!);

        var fullPath = Path.GetFullPath(res.FullPath!);
        var recursive = ParseBool(args, "recursive");
        var nameGlob = ParseString(args, "name_glob");
        var fullPathsShallow = ParseBool(args, "full_paths");
        var maxLines = ParseInt(args, "max_lines", DefaultMaxLines, 1, AbsoluteMaxLines, zeroMeansDefault: true);
        var maxDepth = ParseInt(args, "max_depth", 0, 0, 500, zeroMeansDefault: true);

        if (recursive)
        {
            if (!string.IsNullOrWhiteSpace(nameGlob))
                return Task.FromResult("Error: name_glob is only supported when recursive is false (single-level listing).");
            return Task.FromResult(ListRecursiveFlat(fullPath, maxLines, maxDepth));
        }

        if (fullPathsShallow)
            return Task.FromResult(ListSingleLevelPaths(fullPath, maxLines, nameGlob));

        return Task.FromResult(ListSingleLevelTable(fullPath, nameGlob));
    }

    private static string ListSingleLevelTable(string fullPath, string? nameGlob)
    {
        var entries = Directory.GetFileSystemEntries(fullPath)
            .Select(p => new FileSystemInfoWrapper(p))
            .Where(e => !IsHiddenName(e.Name))
            .Where(e => MatchesNameGlob(e.Name, nameGlob))
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

        return sb.Length > 0 ? sb.ToString() : "(empty)";
    }

    private static string ListSingleLevelPaths(string fullPath, int maxLines, string? nameGlob)
    {
        List<string> lines = [];
        IEnumerable<string> entries;
        try
        {
            entries = Directory.GetFileSystemEntries(fullPath);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }

        foreach (var p in entries.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var fn = Path.GetFileName(p);
            if (IsHiddenName(fn)) continue;
            if (!MatchesNameGlob(fn, nameGlob)) continue;
            if (lines.Count >= maxLines)
            {
                lines.Add("");
                lines.Add(
                    $"--- Truncated: more entries not shown (max_lines={maxLines}). Use path=<subdir> or increase max_lines. ---");
                break;
            }

            lines.Add(Path.GetFullPath(p));
        }

        if (lines.Count == 0)
            return "(empty)";

        var header = new System.Text.StringBuilder();
        header.AppendLine($"Directory (full paths, non-recursive): {fullPath}");
        header.AppendLine();
        return header + string.Join(Environment.NewLine, lines);
    }

    private static string ListRecursiveFlat(string rootFullPath, int maxLines, int maxDepth)
    {
        var lines = new List<string>(Math.Min(maxLines + 8, AbsoluteMaxLines + 8));
        var truncated = false;

        // depthOfDir: 0 = the user-provided path; direct children are depth 1, etc.
        void Walk(string dir, int depthOfDir)
        {
            IEnumerable<string> entries;
            try
            {
                entries = Directory.GetFileSystemEntries(dir);
            }
            catch
            {
                return;
            }

            foreach (var entry in entries.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (IsHiddenName(Path.GetFileName(entry))) continue;
                if (lines.Count >= maxLines)
                {
                    truncated = true;
                    return;
                }

                lines.Add(Path.GetFullPath(entry));

                if (!Directory.Exists(entry))
                    continue;

                var childDepth = depthOfDir + 1;
                var mayRecurse = maxDepth == 0 || childDepth <= maxDepth;
                if (mayRecurse)
                {
                    Walk(entry, childDepth);
                    if (truncated)
                        return;
                }

                if (lines.Count >= maxLines)
                {
                    truncated = true;
                    return;
                }
            }
        }

        Walk(rootFullPath, 0);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Recursive listing (full paths, depth-first): {rootFullPath}");
        sb.AppendLine(maxDepth == 0
            ? "max_depth: unlimited"
            : $"max_depth: {maxDepth} level(s) below path");
        sb.AppendLine($"max_lines: {maxLines}");
        sb.AppendLine();

        foreach (var line in lines)
            sb.AppendLine(line);

        if (truncated)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"--- Truncated at max_lines={maxLines}. " +
                "Narrow with path=<one of the directories above>, increase max_lines, or set max_depth lower. ---");
        }

        return sb.ToString();
    }

    private static bool IsHiddenName(string name) =>
        name.StartsWith('.') && name is not "." and not "..";

    private static bool MatchesNameGlob(string entryName, string? nameGlob)
    {
        if (string.IsNullOrWhiteSpace(nameGlob)) return true;
        return FileSystemName.MatchesSimpleExpression(nameGlob.Trim(), entryName, ignoreCase: true);
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
