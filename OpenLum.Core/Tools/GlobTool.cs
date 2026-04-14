using System.Text;
using System.Text.RegularExpressions;
using OpenLum.Console.Config;
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
    private readonly HashSet<string> _skipDirNames;
    private readonly List<Regex> _skipGlobs;

    public GlobTool(WorkspacePathResolver resolver, SearchConfig? searchConfig = null)
    {
        _resolver = resolver;
        _workspaceRoot = resolver.WorkspaceRoot;
        _skipDirNames = BuildSkipDirs(searchConfig);
        _skipGlobs = BuildSkipGlobs(searchConfig);
    }

    public GlobTool(string workspaceDir) : this(new WorkspacePathResolver(workspaceDir)) { }

    public string Name => "glob";

    public string Description =>
        "Find files by glob pattern. Returns matching file paths sorted by modification time (newest first). " +
        "The host console may show a one-line status like \"Searched files …\" (summary only; full paths are in the tool result). " +
        "Path defaults to workspace root; absolute paths are accepted. " +
        "Patterns not starting with \"**/\" are auto-prepended for recursive matching. " +
        "Prefer simple extension patterns (e.g. \"*.md\") for broad discovery.";

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

        var regex = GlobRegex.ToRegex(pattern);

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
                if (ShouldSkipPath(searchDir, file)) continue;

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

    private bool ShouldSkipPath(string searchDir, string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(dir))
        {
            var name = Path.GetFileName(dir);
            if (_skipDirNames.Contains(name))
                return true;
            dir = Path.GetDirectoryName(dir);
        }

        if (_skipGlobs.Count > 0)
        {
            var rel = Path.GetRelativePath(searchDir, fullPath).Replace('\\', '/');
            foreach (var rx in _skipGlobs)
            {
                if (rx.IsMatch(rel))
                    return true;
            }
        }

        return false;
    }

    private static HashSet<string> BuildSkipDirs(SearchConfig? cfg)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", "bin", "obj", "__pycache__", ".vs", ".idea"
        };
        if (cfg?.SkipDirs is { Count: > 0 })
        {
            foreach (var d in cfg.SkipDirs)
            {
                if (!string.IsNullOrWhiteSpace(d))
                    set.Add(d.Trim());
            }
        }
        return set;
    }

    private static List<Regex> BuildSkipGlobs(SearchConfig? cfg)
    {
        var list = new List<Regex>();
        if (cfg?.SkipGlobs is not { Count: > 0 }) return list;
        foreach (var g in cfg.SkipGlobs)
        {
            if (string.IsNullOrWhiteSpace(g)) continue;
            try { list.Add(GlobRegex.ToRegex(g.Trim())); }
            catch { /* ignore invalid patterns */ }
        }
        return list;
    }
}
