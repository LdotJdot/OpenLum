using System.Text;
using System.Text.RegularExpressions;
using OpenLum.Console.Config;
using OpenLum.Console.Interfaces;
using OpenLum.Console.IO;
using static OpenLum.Console.Tools.ToolArgHelpers;

namespace OpenLum.Console.Tools;

/// <summary>
/// Searches file contents using regex (ripgrep-style). Pure .NET implementation — no external dependency.
/// Supports three output modes: content (default), files_with_matches, count.
/// </summary>
public sealed class GrepTool : ITool
{
    private readonly WorkspacePathResolver _resolver;
    private readonly string _workspaceRoot;
    private const long DefaultMaxBytesPerFile = 5 * 1024 * 1024; // 5MB safety cap
    private readonly HashSet<string> _skipDirNames;
    private readonly List<Regex> _skipGlobs;

    public GrepTool(WorkspacePathResolver resolver, SearchConfig? searchConfig = null)
    {
        _resolver = resolver;
        _workspaceRoot = resolver.WorkspaceRoot;
        _skipDirNames = BuildSkipDirs(searchConfig);
        _skipGlobs = BuildSkipGlobs(searchConfig);
    }

    public GrepTool(string workspaceDir) : this(new WorkspacePathResolver(workspaceDir)) { }

    public string Name => "grep";

    public string Description =>
        "Search file contents using regex. Returns matching lines with file paths and line numbers. " +
        "The host console may show a one-line status like \"Grepped …\" (same idea as the tool result, not extra content). " +
        "Path defaults to workspace root; absolute paths are accepted. " +
        "Use **glob** to narrow by file type; avoid huge path-only patterns. " +
        "Use output_mode=\"files_with_matches\" for file-list-only discovery. " +
        "Prefer this over exec/PowerShell (Get-ChildItem, Select-String) for text search. " +
        "Zero matches is a valid outcome—do not compensate with giant alternation regexes, single-character patterns, or unrelated entities. " +
        "If a literal phrase fails, try at most one or two tight variants (typo, split), then stop and report what you tried.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("pattern", "string", "Regex pattern to search for.", true),
        new ToolParameter("path", "string", "Directory or file to search (default: workspace root).", false),
        new ToolParameter("glob", "string", "Optional file glob filter.", false),
        new ToolParameter("case_insensitive", "boolean", "Ignore case (default false).", false),
        new ToolParameter("head_limit", "number", "Max matches to return (default 50, max 200).", false),
        new ToolParameter("context_lines", "number", "Lines of context around each match (default 0, max 5).", false),
        new ToolParameter("max_bytes", "number", "Max file size in bytes per file (default 5242880 = 5MB).", false),
        new ToolParameter("output_mode", "string",
            "\"content\" (default): matching lines with context. " +
            "\"files_with_matches\": only file paths. " +
            "\"count\": match counts per file.", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var patternStr = ParseString(args, "pattern");
        if (string.IsNullOrWhiteSpace(patternStr))
            return Task.FromResult("Error: pattern is required.");

        var pathArg = ParseString(args, "path");
        if (string.IsNullOrWhiteSpace(pathArg)) pathArg = ".";
        var globFilter = ParseString(args, "glob");
        var ignoreCase = ParseBool(args, "case_insensitive");
        var headLimit = ParseInt(args, "head_limit", 50, 1, 200, zeroMeansDefault: true);
        var contextLines = ParseInt(args, "context_lines", 0, 0, 5);
        var maxBytes = (long)ParseInt(args, "max_bytes", (int)DefaultMaxBytesPerFile, 1, int.MaxValue, zeroMeansDefault: true);
        var outputMode = (ParseString(args, "output_mode") ?? "content").ToLowerInvariant();

        var res = _resolver.Resolve(pathArg);
        if (!res.IsOk)
            return Task.FromResult(res.Error!);
        var searchPath = res.FullPath!;

        Regex regex;
        try
        {
            var opts = RegexOptions.Compiled;
            if (ignoreCase) opts |= RegexOptions.IgnoreCase;
            regex = new Regex(patternStr, opts, TimeSpan.FromSeconds(5));
        }
        catch (RegexParseException ex)
        {
            return Task.FromResult(
                $"Error: invalid regex: {ex.Message}. " +
                "If you meant a literal dot, parenthesis, or bracket, escape regex metacharacters. For awkward fixed text, use a shorter unique substring or a minimal pattern.");
        }

        IEnumerable<string> files;
        if (File.Exists(searchPath))
            files = [searchPath];
        else if (Directory.Exists(searchPath))
            files = EnumerateFilesWithSkips(searchPath, globFilter);
        else
            return Task.FromResult($"Error: path not found: {pathArg}");

        return outputMode switch
        {
            "files_with_matches" => Task.FromResult(SearchFilesOnly(files, regex, headLimit, maxBytes, ct)),
            "count" => Task.FromResult(SearchCount(files, regex, headLimit, maxBytes, ct)),
            _ => Task.FromResult(SearchContent(files, regex, headLimit, contextLines, maxBytes, ct))
        };
    }

    private string SearchContent(IEnumerable<string> files, Regex regex, int headLimit, int contextLines, long maxBytes, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var matchCount = 0;
        var fileCount = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested || matchCount >= headLimit) break;
            if (IsBinaryFile(file)) continue;
            if (IsTooLarge(file, maxBytes)) continue;

            var relativePath = Path.GetRelativePath(_workspaceRoot, file).Replace('\\', '/');
            var fileHasMatch = false;
            var prev = new Queue<(int LineNo, string Text)>(capacity: Math.Max(0, contextLines));
            var postRemaining = 0;
            var lastPrintedLine = 0;
            var pendingSeparator = false;

            IEnumerable<string> lineEnum;
            try { lineEnum = File.ReadLines(file); }
            catch { continue; }

            var lineNo = 0;
            foreach (var line in lineEnum)
            {
                if (ct.IsCancellationRequested || matchCount >= headLimit) break;
                lineNo++;

                if (pendingSeparator)
                {
                    // Separator between match blocks when context_lines > 0.
                    sb.AppendLine("--");
                    pendingSeparator = false;
                }

                var isMatch = regex.IsMatch(line);
                if (isMatch)
                {
                    if (!fileHasMatch)
                    {
                        if (fileCount > 0) sb.AppendLine();
                        sb.AppendLine(relativePath);
                        fileHasMatch = true;
                        fileCount++;
                    }

                    // Print pre-context
                    foreach (var (ln, txt) in prev)
                    {
                        if (ln <= lastPrintedLine) continue;
                        sb.AppendLine($"{ln}-{txt}");
                        lastPrintedLine = ln;
                    }

                    // Print match line
                    if (lineNo > lastPrintedLine)
                    {
                        sb.AppendLine($"{lineNo}:{line}");
                        lastPrintedLine = lineNo;
                    }

                    postRemaining = contextLines;
                    matchCount++;
                }
                else if (postRemaining > 0)
                {
                    if (lineNo > lastPrintedLine)
                    {
                        sb.AppendLine($"{lineNo}-{line}");
                        lastPrintedLine = lineNo;
                    }
                    postRemaining--;
                    if (postRemaining == 0 && contextLines > 0)
                        pendingSeparator = true;
                }

                if (contextLines > 0)
                {
                    if (prev.Count == contextLines) prev.Dequeue();
                    prev.Enqueue((lineNo, line));
                }
            }
        }

        if (matchCount == 0)
            return "No matches found.";

        if (matchCount >= headLimit)
            sb.AppendLine($"\n(showing first {headLimit} matches; use a narrower pattern or path to see more)");

        return sb.ToString();
    }

    private string SearchFilesOnly(IEnumerable<string> files, Regex regex, int headLimit, long maxBytes, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var count = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested || count >= headLimit) break;
            if (IsBinaryFile(file)) continue;
            if (IsTooLarge(file, maxBytes)) continue;

            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (!regex.IsMatch(line)) continue;
                    sb.AppendLine(Path.GetRelativePath(_workspaceRoot, file).Replace('\\', '/'));
                    count++;
                    break;
                }
            }
            catch { /* skip unreadable */ }
        }

        return count == 0
            ? "No matches found."
            : sb.ToString();
    }

    private string SearchCount(IEnumerable<string> files, Regex regex, int headLimit, long maxBytes, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var fileCount = 0;
        var totalMatches = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested || fileCount >= headLimit) break;
            if (IsBinaryFile(file)) continue;
            if (IsTooLarge(file, maxBytes)) continue;

            var matches = 0;
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (regex.IsMatch(line)) matches++;
                }
            }
            catch { continue; }

            if (matches > 0)
            {
                sb.AppendLine($"{Path.GetRelativePath(_workspaceRoot, file).Replace('\\', '/')}:{matches}");
                fileCount++;
                totalMatches += matches;
            }
        }

        if (totalMatches == 0)
            return "No matches found.";

        sb.AppendLine($"\n{totalMatches} matches in {fileCount} files");
        return sb.ToString();
    }

    /// <summary>Default directories to skip by path segment.</summary>
    private static readonly HashSet<string> DefaultSkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", "__pycache__", ".vs", ".idea"
    };

    private IEnumerable<string> EnumerateFilesWithSkips(string directory, string? globFilter)
    {
        var extensions = ParseGlobExtensions(globFilter);

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*", new EnumerationOptions
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
            if (ShouldSkipPath(directory, f)) continue;

            if (extensions is not null)
            {
                var ext = Path.GetExtension(f);
                if (!extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    continue;
            }

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

    private static HashSet<string>? ParseGlobExtensions(string? glob)
    {
        if (string.IsNullOrWhiteSpace(glob)) return null;

        var cleaned = glob.Trim().Replace('\\', '/');

        // Accept common recursive patterns like "**/*.md" by stripping any path prefix.
        // This tool's glob is an extension filter, not a full path glob.
        var slash = cleaned.LastIndexOf('/');
        if (slash >= 0)
            cleaned = cleaned[(slash + 1)..];

        if (cleaned.StartsWith("*.{") && cleaned.EndsWith("}"))
        {
            var inner = cleaned[3..^1];
            return inner.Split(',').Select(e => "." + e.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (cleaned.StartsWith("*."))
            return [cleaned[1..]];

        return null;
    }

    private static bool IsBinaryFile(string path)
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
        var set = new HashSet<string>(DefaultSkipDirNames, StringComparer.OrdinalIgnoreCase);
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
