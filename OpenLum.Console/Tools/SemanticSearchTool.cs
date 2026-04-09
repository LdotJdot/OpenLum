using System.Text;
using System.Text.RegularExpressions;
using OpenLum.Console.Config;
using OpenLum.Console.Interfaces;
using OpenLum.Console.IO;
using static OpenLum.Console.Tools.ToolArgHelpers;

namespace OpenLum.Console.Tools;

/// <summary>
/// v0 semantic_search: lightweight heuristic search that scores files by keyword hits
/// in path and contents, then returns top-ranked snippets.
/// No indexing; designed to reduce "I don't know the exact symbol" round-trips.
/// </summary>
public sealed class SemanticSearchTool : ITool
{
    private readonly WorkspacePathResolver _resolver;
    private readonly string _workspaceRoot;
    private readonly HashSet<string> _skipDirNames;
    private readonly List<Regex> _skipGlobs;

    private const long DefaultMaxBytesPerFile = 5 * 1024 * 1024; // 5MB

    public SemanticSearchTool(WorkspacePathResolver resolver, SearchConfig? searchConfig = null)
    {
        _resolver = resolver;
        _workspaceRoot = resolver.WorkspaceRoot;
        _skipDirNames = BuildSkipDirs(searchConfig);
        _skipGlobs = BuildSkipGlobs(searchConfig);
    }

    public string Name => "semantic_search";

    public string Description =>
        "Heuristic semantic search (v0): find relevant files by meaning using keyword scoring over paths and contents. " +
        "Returns top results with short snippets. Prefer this when you don't know exact symbols. " +
        "Planned v1: add an index (inverted index or embeddings) for better recall and speed.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("query", "string", "A natural-language query describing what you're looking for.", true),
        new ToolParameter("path", "string", "Directory to search (default: workspace root).", false),
        new ToolParameter("glob", "string", "Optional file glob filter (e.g. \"*.cs\", \"*.{ts,tsx}\").", false),
        new ToolParameter("head_limit", "number", "Max results (default 20, max 100).", false),
        new ToolParameter("max_bytes", "number", "Max file size in bytes per file (default 5242880 = 5MB).", false),
        new ToolParameter("max_lines", "number", "Max lines to scan per file (default 400, max 2000).", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var query = ParseString(args, "query");
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult("Error: query is required.");

        var pathArg = ParseString(args, "path");
        if (string.IsNullOrWhiteSpace(pathArg)) pathArg = ".";

        var glob = ParseString(args, "glob");
        var headLimit = ParseInt(args, "head_limit", 20, 1, 100, zeroMeansDefault: true);
        var maxBytes = (long)ParseInt(args, "max_bytes", (int)DefaultMaxBytesPerFile, 1, int.MaxValue, zeroMeansDefault: true);
        var maxLines = ParseInt(args, "max_lines", 400, 1, 2000, zeroMeansDefault: true);

        var res = _resolver.ResolveExistingDirectory(pathArg);
        if (!res.IsOk)
            return Task.FromResult(res.Error!);
        var root = res.FullPath!;

        var tokens = Tokenize(query);
        if (tokens.Count == 0)
            return Task.FromResult("No tokens extracted from query. Provide a more specific query.");

        var extFilter = GrepExtensions(glob);

        var scored = new List<Result>();
        foreach (var file in EnumerateFiles(root))
        {
            if (ct.IsCancellationRequested) break;
            if (IsBinaryByExt(file)) continue;
            if (IsTooLarge(file, maxBytes)) continue;
            if (extFilter is not null && !extFilter.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                continue;

            var rel = Path.GetRelativePath(_workspaceRoot, file).Replace('\\', '/');
            var score = 0;
            foreach (var t in tokens)
            {
                if (rel.Contains(t, StringComparison.OrdinalIgnoreCase))
                    score += 5;
            }

            int? firstLineNo = null;
            string? firstLine = null;
            var lineNo = 0;
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (ct.IsCancellationRequested) break;
                    lineNo++;
                    if (lineNo > maxLines) break;
                    foreach (var t in tokens)
                    {
                        if (line.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            score += 1;
                            if (firstLineNo is null)
                            {
                                firstLineNo = lineNo;
                                firstLine = line.TrimEnd();
                            }
                        }
                    }
                }
            }
            catch
            {
                continue;
            }

            if (score <= 0) continue;
            scored.Add(new Result(rel, score, firstLineNo, firstLine));
        }

        if (scored.Count == 0)
            return Task.FromResult("No matches found.");

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (scored.Count > headLimit)
            scored = scored.Take(headLimit).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Query tokens: {string.Join(", ", tokens)}");
        sb.AppendLine();
        foreach (var r in scored)
        {
            sb.AppendLine($"{r.Score,3}  {r.Path}");
            if (r.LineNo is { } ln && !string.IsNullOrWhiteSpace(r.Line))
                sb.AppendLine($"     {ln}: {r.Line}");
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    private IEnumerable<string> EnumerateFiles(string root)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            });
        }
        catch
        {
            yield break;
        }

        foreach (var f in files)
        {
            if (ShouldSkipPath(root, f)) continue;
            yield return f;
        }
    }

    private bool ShouldSkipPath(string searchRoot, string fullPath)
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
            var rel = Path.GetRelativePath(searchRoot, fullPath).Replace('\\', '/');
            foreach (var rx in _skipGlobs)
            {
                if (rx.IsMatch(rel))
                    return true;
            }
        }

        return false;
    }

    private static List<string> Tokenize(string query)
    {
        // Extract a few "useful" tokens from natural language.
        var tokens = new List<string>();
        foreach (Match m in Regex.Matches(query, @"[\p{L}\p{N}_]{2,}"))
        {
            var t = m.Value.Trim();
            if (t.Length < 2) continue;
            if (tokens.Contains(t, StringComparer.OrdinalIgnoreCase)) continue;
            tokens.Add(t);
            if (tokens.Count >= 6) break;
        }
        return tokens;
    }

    private static HashSet<string>? GrepExtensions(string? glob)
    {
        if (string.IsNullOrWhiteSpace(glob)) return null;
        glob = glob.Trim();
        if (glob.StartsWith("*.{") && glob.EndsWith("}"))
        {
            var inner = glob[3..^1];
            return inner.Split(',').Select(e => "." + e.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        if (glob.StartsWith("*."))
            return [glob[1..]];
        return null;
    }

    private static bool IsBinaryByExt(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".exe" or ".dll" or ".pdb" or ".obj" or ".o" or ".so" or ".dylib"
            or ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp"
            or ".zip" or ".gz" or ".tar" or ".7z" or ".rar"
            or ".pdf" or ".docx" or ".xlsx" or ".pptx"
            or ".woff" or ".woff2" or ".ttf" or ".eot"
            or ".mp3" or ".mp4" or ".avi" or ".mov"
            or ".sqlite" or ".db" or ".nupkg";
    }

    private static bool IsTooLarge(string path, long maxBytes)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists && fi.Length > maxBytes;
        }
        catch
        {
            return false;
        }
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

    private readonly record struct Result(string Path, int Score, int? LineNo, string? Line);
}

