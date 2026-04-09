using System.Text;
using System.Text.RegularExpressions;
using OpenLum.Console.Interfaces;
using OpenLum.Console.IO;
using static OpenLum.Console.Tools.ToolArgHelpers;

namespace OpenLum.Console.Tools;

/// <summary>
/// Finds files by name/path pattern (glob). Useful for discovering files before reading or grepping.
/// </summary>
public sealed class GlobTool : ITool
{
    private readonly WorkspacePathResolver _resolver;
    private readonly string _workspaceRoot;

    public GlobTool(WorkspacePathResolver resolver)
    {
        _resolver = resolver;
        _workspaceRoot = resolver.WorkspaceRoot;
    }

    public GlobTool(string workspaceDir) : this(new WorkspacePathResolver(workspaceDir)) { }

    public string Name => "glob";

    public string Description =>
        "Find files by glob pattern. Returns matching file paths sorted by modification time (newest first). " +
        "Path defaults to workspace root; any absolute path is also accepted. " +
        "Patterns not starting with \"**/\" are auto-prepended for recursive matching.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("pattern", "string", "Glob pattern (e.g. \"*.cs\", \"**/test_*.py\", \"src/**/*.ts\").", true),
        new ToolParameter("path", "string", "Directory to search (default: workspace root).", false),
        new ToolParameter("head_limit", "number", "Max results (default 50, max 200).", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var pattern = ParseString(args, "pattern");
        if (string.IsNullOrWhiteSpace(pattern))
            return Task.FromResult("Error: pattern is required.");

        var pathArg = ParseString(args, "path");
        if (string.IsNullOrWhiteSpace(pathArg)) pathArg = ".";

        var headLimit = ParseInt(args, "head_limit", 50, 1, 200, zeroMeansDefault: true);

        var res = _resolver.ResolveExistingDirectory(pathArg);
        if (!res.IsOk)
            return Task.FromResult(res.Error!);
        var searchDir = res.FullPath!;

        var regex = GlobToRegex(pattern);

        List<(string RelPath, DateTime ModTime)> matches = [];

        try
        {
            foreach (var file in Directory.EnumerateFiles(searchDir, "*", new EnumerationOptions
                     {
                         RecurseSubdirectories = true,
                         IgnoreInaccessible = true,
                         AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
                     }))
            {
                if (ct.IsCancellationRequested) break;
                if (ShouldSkipPath(file)) continue;

                var rel = Path.GetRelativePath(searchDir, file).Replace('\\', '/');

                if (!regex.IsMatch(rel) && !regex.IsMatch(Path.GetFileName(file)))
                    continue;

                var modTime = File.GetLastWriteTimeUtc(file);
                matches.Add((rel, modTime));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error enumerating files: {ex.Message}");
        }

        if (matches.Count == 0)
            return Task.FromResult("No files matched the pattern.");

        matches.Sort((a, b) => b.ModTime.CompareTo(a.ModTime));

        var sb = new StringBuilder();
        var shown = Math.Min(matches.Count, headLimit);
        for (var i = 0; i < shown; i++)
            sb.AppendLine(matches[i].RelPath);

        if (matches.Count > headLimit)
            sb.AppendLine($"\n({matches.Count} total matches; showing first {headLimit})");

        return Task.FromResult(sb.ToString());
    }

    /// <summary>Skip directories by path segment, not substring contains.</summary>
    private static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", "__pycache__", ".vs", ".idea"
    };

    private static bool ShouldSkipPath(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(dir))
        {
            var name = Path.GetFileName(dir);
            if (SkipDirNames.Contains(name))
                return true;
            dir = Path.GetDirectoryName(dir);
        }
        return false;
    }

    /// <summary>
    /// Convert a glob pattern to a regex. Supports *, **, ?, {a,b}.
    /// Patterns without path separators get auto-prepended with **/ for recursive matching.
    /// </summary>
    private static Regex GlobToRegex(string glob)
    {
        if (!glob.Contains('/') && !glob.Contains('\\') && !glob.StartsWith("**/"))
            glob = "**/" + glob;

        var sb = new StringBuilder("^");
        var i = 0;
        while (i < glob.Length)
        {
            var c = glob[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        if (i + 2 < glob.Length && (glob[i + 2] == '/' || glob[i + 2] == '\\'))
                        {
                            sb.Append("(.+/)?");
                            i += 3;
                        }
                        else
                        {
                            sb.Append(".*");
                            i += 2;
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                        i++;
                    }
                    break;
                case '?':
                    sb.Append("[^/]");
                    i++;
                    break;
                case '{':
                    var end = glob.IndexOf('}', i);
                    if (end > i)
                    {
                        var alts = glob[(i + 1)..end].Split(',');
                        sb.Append('(');
                        sb.Append(string.Join('|', alts.Select(Regex.Escape)));
                        sb.Append(')');
                        i = end + 1;
                    }
                    else
                    {
                        sb.Append(Regex.Escape(c.ToString()));
                        i++;
                    }
                    break;
                case '\\':
                case '/':
                    sb.Append("[/\\\\]");
                    i++;
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    i++;
                    break;
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
