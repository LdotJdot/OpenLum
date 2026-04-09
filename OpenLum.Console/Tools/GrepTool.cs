using System.Text;
using System.Text.RegularExpressions;
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

    public GrepTool(WorkspacePathResolver resolver)
    {
        _resolver = resolver;
        _workspaceRoot = resolver.WorkspaceRoot;
    }

    public GrepTool(string workspaceDir) : this(new WorkspacePathResolver(workspaceDir)) { }

    public string Name => "grep";

    public string Description =>
        "Search file contents using regex. Returns matching lines with file paths and line numbers. " +
        "Path defaults to workspace root; any absolute path is also accepted. " +
        "Use glob parameter to filter file types (e.g. \"*.cs\"). " +
        "Use output_mode=\"files_with_matches\" for file-list-only discovery. " +
        "Use this tool instead of exec+rg for text search.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("pattern", "string", "Regex pattern to search for.", true),
        new ToolParameter("path", "string", "Directory or file to search (default: workspace root).", false),
        new ToolParameter("glob", "string", "File glob filter (e.g. \"*.cs\", \"*.{ts,tsx}\").", false),
        new ToolParameter("case_insensitive", "boolean", "Ignore case (default false).", false),
        new ToolParameter("head_limit", "number", "Max matches to return (default 50, max 200).", false),
        new ToolParameter("context_lines", "number", "Lines of context around each match (default 0, max 5).", false),
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
            return Task.FromResult($"Error: invalid regex: {ex.Message}");
        }

        IEnumerable<string> files;
        if (File.Exists(searchPath))
            files = [searchPath];
        else if (Directory.Exists(searchPath))
            files = EnumerateFiles(searchPath, globFilter);
        else
            return Task.FromResult($"Error: path not found: {pathArg}");

        return outputMode switch
        {
            "files_with_matches" => Task.FromResult(SearchFilesOnly(files, regex, headLimit, ct)),
            "count" => Task.FromResult(SearchCount(files, regex, headLimit, ct)),
            _ => Task.FromResult(SearchContent(files, regex, headLimit, contextLines, ct))
        };
    }

    private string SearchContent(IEnumerable<string> files, Regex regex, int headLimit, int contextLines, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var matchCount = 0;
        var fileCount = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested || matchCount >= headLimit) break;
            if (IsBinaryFile(file)) continue;

            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch { continue; }

            var relativePath = Path.GetRelativePath(_workspaceRoot, file).Replace('\\', '/');
            var fileHasMatch = false;

            for (var i = 0; i < lines.Length && matchCount < headLimit; i++)
            {
                if (!regex.IsMatch(lines[i])) continue;

                if (!fileHasMatch)
                {
                    if (fileCount > 0) sb.AppendLine();
                    sb.AppendLine(relativePath);
                    fileHasMatch = true;
                    fileCount++;
                }

                var startCtx = Math.Max(0, i - contextLines);
                var endCtx = Math.Min(lines.Length - 1, i + contextLines);

                for (var c = startCtx; c <= endCtx; c++)
                {
                    var prefix = c == i ? ":" : "-";
                    sb.AppendLine($"{c + 1}{prefix}{lines[c]}");
                }

                if (contextLines > 0 && endCtx < lines.Length - 1)
                    sb.AppendLine("--");

                matchCount++;
            }
        }

        if (matchCount == 0)
            return "No matches found.";

        if (matchCount >= headLimit)
            sb.AppendLine($"\n(showing first {headLimit} matches; use a narrower pattern or path to see more)");

        return sb.ToString();
    }

    private string SearchFilesOnly(IEnumerable<string> files, Regex regex, int headLimit, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var count = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested || count >= headLimit) break;
            if (IsBinaryFile(file)) continue;

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

    private string SearchCount(IEnumerable<string> files, Regex regex, int headLimit, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var fileCount = 0;
        var totalMatches = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested || fileCount >= headLimit) break;
            if (IsBinaryFile(file)) continue;

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

    /// <summary>Skip directories by path segment, not substring contains.</summary>
    private static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", "__pycache__", ".vs", ".idea"
    };

    private static IEnumerable<string> EnumerateFiles(string directory, string? globFilter)
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
            if (ShouldSkipPath(f)) continue;

            if (extensions is not null)
            {
                var ext = Path.GetExtension(f);
                if (!extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    continue;
            }

            yield return f;
        }
    }

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

    private static HashSet<string>? ParseGlobExtensions(string? glob)
    {
        if (string.IsNullOrWhiteSpace(glob)) return null;

        var cleaned = glob.Trim();
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
}
