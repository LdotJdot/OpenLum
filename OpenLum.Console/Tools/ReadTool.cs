using System.Text;
using OpenLum.Console.Interfaces;
using OpenLum.Console.IO;
using static OpenLum.Console.Tools.ToolArgHelpers;

namespace OpenLum.Console.Tools;

/// <summary>
/// Reads plain text file contents from the workspace. For Office/PDF/CAD files, use the read skill exes via exec.
/// Also allows reading from skill directories (for loading SKILL.md on demand).
/// </summary>
public sealed class ReadTool : ITool
{
    private readonly WorkspacePathResolver _resolver;
    private const long DefaultMaxBytes = 5 * 1024 * 1024; // 5MB safety cap for plain-text reads

    public ReadTool(WorkspacePathResolver resolver) => _resolver = resolver;

    public ReadTool(string workspaceDir, IReadOnlyList<string>? extraReadRoots = null)
        : this(new WorkspacePathResolver(workspaceDir, extraReadRoots)) { }

    public string Name => "read";

    public string Description =>
        "Read a plain-text file (source code, txt, md, json, etc.). " +
        "For PDF/Office/CAD, use the corresponding skill via exec. " +
        "Path: workspace-relative or any absolute path. Supports ~.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("path", "string", "File path (workspace-relative or absolute; supports ~).", true),
        new ToolParameter("offset", "number", "1-based start line (default 1). Negative values count from end.", false),
        new ToolParameter("limit", "number", "Max lines to return (default 200, max 2000).", false),
        new ToolParameter("max_bytes", "number", "Max file size in bytes to read (default 5242880 = 5MB).", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var pathArg = args.GetValueOrDefault("path")?.ToString()?.Trim();
        var res = _resolver.ResolveExistingFile(pathArg);
        if (!res.IsOk)
            return Task.FromResult(res.Error!);

        var fullPath = res.FullPath!;

        var limit = ParseInt(args, "limit", 200, 1, 2000, zeroMeansDefault: true);
        var offset = ParseInt(args, "offset", 1);
        var maxBytes = (long)ParseInt(args, "max_bytes", (int)DefaultMaxBytes, 1, int.MaxValue, zeroMeansDefault: true);

        if (IsSkillFile(fullPath))
        {
            var skillName = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? "skill";
            LogSkillLoaded(skillName, fullPath);
        }

        try
        {
            var fi = new FileInfo(fullPath);
            if (fi.Exists && fi.Length > maxBytes)
            {
                return Task.FromResult(
                    $"Error reading file: file is too large ({fi.Length} bytes > max_bytes={maxBytes}). " +
                    "Use grep to locate the relevant section, then read with a smaller range via offset/limit.");
            }
        }
        catch
        {
            // ignore size probing failures; attempt read below
        }

        try
        {
            return Task.FromResult(ReadRangeStreaming(fullPath, offset, limit));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                $"Error reading file: {ex.Message}. For Office/PDF/CAD, use exec with the corresponding skill exe.");
        }
    }

    private static bool IsSkillFile(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        return string.Equals(fileName, "SKILL.md", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly object ConsoleLock = new();

    private static void LogSkillLoaded(string skillName, string fullPath)
    {
        lock (ConsoleLock)
        {
            var prev = System.Console.ForegroundColor;
            try
            {
                System.Console.ForegroundColor = System.ConsoleColor.Green;
                System.Console.WriteLine($"  [skill] Loaded: {skillName} ({fullPath})");
            }
            finally
            {
                System.Console.ForegroundColor = prev;
            }
        }
    }

    private static string ReadRangeStreaming(string fullPath, int offset, int limit)
    {
        // Two-pass for negative offsets: count lines first, then stream the requested range.
        var startLine = offset;
        int totalLines = 0;
        if (offset < 0)
        {
            foreach (var _ in File.ReadLines(fullPath))
                totalLines++;
            startLine = Math.Max(1, totalLines + offset + 1); // offset=-1 => last line
        }

        var sb = new StringBuilder();
        var lineNo = 0;
        var startIdx = Math.Max(1, startLine);
        var endIdx = startIdx + Math.Max(1, limit) - 1;

        foreach (var line in File.ReadLines(fullPath))
        {
            lineNo++;
            if (lineNo < startIdx) continue;
            if (lineNo > endIdx) break;
            sb.AppendLine($"{lineNo,6}|{line}");
        }

        // If start beyond EOF, lineNo will be total lines (or less if file empty).
        if (sb.Length == 0)
        {
            if (offset < 0)
                return $"(file has {totalLines} lines; requested range is empty)";
            // For positive offset, we need a count for the message; do a cheap count only on empty result.
            foreach (var _ in File.ReadLines(fullPath))
                totalLines++;
            return $"(file has {totalLines} lines; requested range is empty)";
        }

        // Add trailing hint only when we know total lines cheaply.
        if (offset < 0)
        {
            if (endIdx < totalLines)
                sb.AppendLine($"... [{totalLines - endIdx} more lines, {totalLines} total]");
        }

        return sb.ToString();
    }
}
