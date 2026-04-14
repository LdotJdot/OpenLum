using System.Text;
using OpenLum.Console.Extractors;
using OpenLum.Console.Interfaces;
using OpenLum.Console.IO;
using static OpenLum.Console.Tools.ToolArgHelpers;

namespace OpenLum.Console.Tools;

/// <summary>
/// Reads plain text by line range, or extracts text from PDF/Office/CAD via bundled exes under InternalTools/read/.
/// Same tool name and parameters; continuation is always next_offset = offset + limit (see description).
/// </summary>
public sealed class ReadTool : ITool
{
    private readonly WorkspacePathResolver _resolver;
    private const long DefaultMaxBytes = 5 * 1024 * 1024; // 5MB safety cap for plain-text reads
    private const int DefaultExtractCharLimit = 2000;

    public ReadTool(WorkspacePathResolver resolver) => _resolver = resolver;

    public ReadTool(string workspaceDir, IReadOnlyList<string>? extraReadRoots = null)
        : this(new WorkspacePathResolver(workspaceDir, extraReadRoots)) { }

    public string Name => "read";

    public string Description =>
        "**Plain text**: offset/limit are 1-based **line** numbers; next window starts at offset+limit (no overlap). " +
        "**Extracted formats** (PDF, Word .docx, Excel, PowerPoint, CAD, etc.—host-bundled extractors): offset is 0-based **character** index in extracted text; next_offset = offset + limit. " +
        "For Office and PDF paths, **always use this read tool**—do not use shell/exec, Python, or COM automation to open them; those are slower, brittle, and wrong here. " +
        "Defaults depend on file kind; omit offset/limit to use them. Path: workspace-relative or absolute; supports ~.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("path", "string", "File path (workspace-relative or absolute; supports ~).", true),
        new ToolParameter("offset", "number",
            "Plain text: 1-based line start (negative = from EOF). Extracted: 0-based char start. Next: offset+limit.",
            false),
        new ToolParameter("limit", "number",
            "Window size (lines for plain text, characters for extracted). Next: offset+limit.",
            false),
        new ToolParameter("max_bytes", "number", "Plain text only: max file size in bytes (default 5242880 = 5MB).", false)
    ];

    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var pathArg = args.GetValueOrDefault("path")?.ToString()?.Trim();
        var res = _resolver.ResolveExistingFile(pathArg);
        if (!res.IsOk)
            return res.Error!;

        var fullPath = res.FullPath!;
        var ext = Path.GetExtension(fullPath);

        if (IsSkillFile(fullPath))
        {
            var skillName = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? "skill";
            LogSkillLoaded(skillName, fullPath);
        }

        if (ExeReadDispatcher.IsSupportedExtractExtension(ext))
        {
            var start = ParseInt(args, "offset", 0, 0, int.MaxValue);
            var lim = ParseInt(args, "limit", DefaultExtractCharLimit, 1, 2_000_000, zeroMeansDefault: true);
            try
            {
                return await ExeReadDispatcher.ExtractTextAsync(fullPath, start, lim, ct);
            }
            catch (Exception ex)
            {
                return
                    $"Error: [read] Unexpected failure while extracting text: {ex.Message}. " +
                    "If this persists for the same path, treat the file as unreadable here; do not try unrelated tools.";
            }
        }

        var limit = ParseInt(args, "limit", 200, 1, 2000, zeroMeansDefault: true);
        var offset = ParseInt(args, "offset", 1);
        var maxBytes = (long)ParseInt(args, "max_bytes", (int)DefaultMaxBytes, 1, int.MaxValue, zeroMeansDefault: true);

        try
        {
            var fi = new FileInfo(fullPath);
            if (fi.Exists && fi.Length > maxBytes)
            {
                return
                    $"Error reading file: file is too large ({fi.Length} bytes > max_bytes={maxBytes}). " +
                    "Use grep to locate the relevant section, then read with a smaller range via offset/limit.";
            }
        }
        catch
        {
            // ignore size probing failures; attempt read below
        }

        try
        {
            return ReadRangeStreaming(fullPath, offset, limit);
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
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
