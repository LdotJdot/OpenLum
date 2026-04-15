using System.Linq;
using System.Text.Json;
using OpenLum.Console.Editing;
using OpenLum.Console.Interfaces;
using OpenLum.Console.IO;
using static OpenLum.Console.Tools.ToolArgHelpers;

namespace OpenLum.Console.Tools;

/// <summary>
/// Line-based plain-text edits (UTF-8). Not for .docx/PDF. Prefer str_replace for unique substring replacement.
/// </summary>
public sealed class TextEditTool : ITool
{
    private readonly WorkspacePathResolver _resolver;

    public TextEditTool(WorkspacePathResolver resolver) => _resolver = resolver;

    public string Name => "text_edit";

    public string Description =>
        "Plain-text / source file edits by line number (1-based) or whole-file literal/regex replace. " +
        "UTF-8 only; not for Office/PDF/binary. " +
        "Operations: read_range, replace_range, replace_all, replace_first, replace_all_regex, insert_lines, delete_range, append_lines. " +
        "Prefer str_replace for small unique snippets; for long blocks use read_range to get line numbers then replace_range (avoids giant old_string). " +
        "replace_first/replace_all share the same LF/CRLF tolerance as str_replace when matching old_string.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("path", "string", "File path (workspace-relative or absolute).", true),
        new ToolParameter("operation", "string",
            "read_range | replace_range | replace_all | replace_first | replace_all_regex | insert_lines | delete_range | append_lines", true),
        new ToolParameter("start_line", "number", "Start line (1-based) for read_range, replace_range, delete_range.", false),
        new ToolParameter("end_line", "number", "End line (1-based, inclusive) for read_range, replace_range, delete_range.", false),
        new ToolParameter("after_line", "number", "Insert after this line (1-based) for insert_lines.", false),
        new ToolParameter("new_content", "string", "Multi-line replacement for replace_range (use \\n for newlines).", false),
        new ToolParameter("lines", "array", "Array of strings for insert_lines / append_lines (alternative to new_content).", false),
        new ToolParameter("old_string", "string", "Literal find text for replace_all / replace_first.", false),
        new ToolParameter("new_string", "string", "Replacement for replace_all / replace_first.", false),
        new ToolParameter("pattern", "string", "Regex pattern for replace_all_regex.", false),
        new ToolParameter("replacement", "string", "Regex replacement for replace_all_regex (may use $1 etc.).", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var pathArg = args.GetValueOrDefault("path")?.ToString()?.Trim();
        var op = args.GetValueOrDefault("operation")?.ToString()?.Trim().ToLowerInvariant();

        var res = _resolver.ResolveExistingFile(pathArg);
        if (!res.IsOk)
            return Task.FromResult(res.Error!);

        var fullPath = res.FullPath!;
        if (string.IsNullOrEmpty(op))
            return Task.FromResult("Error: operation is required.");

        try
        {
            var msg = op switch
            {
                "read_range" => DoReadRange(fullPath, args),
                "replace_range" => DoReplaceRange(fullPath, args),
                "replace_all" => DoReplaceAll(fullPath, args),
                "replace_first" => DoReplaceFirst(fullPath, args),
                "replace_all_regex" => DoReplaceAllRegex(fullPath, args),
                "insert_lines" => DoInsertLines(fullPath, args),
                "delete_range" => DoDeleteRange(fullPath, args),
                "append_lines" => DoAppendLines(fullPath, args),
                _ => $"Error: unknown operation '{op}'."
            };
            return Task.FromResult(msg);
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    private static string DoReadRange(string fullPath, IReadOnlyDictionary<string, object?> args)
    {
        var s = ParseInt(args, "start_line", 1, 1, int.MaxValue);
        var e = ParseInt(args, "end_line", s, 1, int.MaxValue);
        return PlainTextLineEditor.ReadRange(fullPath, s, e);
    }

    private static string DoReplaceRange(string fullPath, IReadOnlyDictionary<string, object?> args)
    {
        var s = ParseInt(args, "start_line", 1, 1, int.MaxValue);
        var e = ParseInt(args, "end_line", s, 1, int.MaxValue);
        var newLines = LinesOrContent(args);
        if (newLines.Count == 0)
            return "Error: new_content or non-empty lines is required for replace_range.";
        PlainTextLineEditor.ReplaceRangeWithText(fullPath, s, e, newLines);
        return $"Replaced lines {s}-{e} in {Path.GetFileName(fullPath)}.";
    }

    private static string DoReplaceAll(string fullPath, IReadOnlyDictionary<string, object?> args)
    {
        var oldS = args.GetValueOrDefault("old_string")?.ToString();
        var newS = args.GetValueOrDefault("new_string")?.ToString();
        if (string.IsNullOrEmpty(oldS))
            return "Error: old_string is required.";
        if (newS is null)
            return "Error: new_string is required.";
        PlainTextLineEditor.ReplaceAll(fullPath, oldS, newS);
        return $"replace_all applied to {Path.GetFileName(fullPath)}.";
    }

    private static string DoReplaceFirst(string fullPath, IReadOnlyDictionary<string, object?> args)
    {
        var oldS = args.GetValueOrDefault("old_string")?.ToString();
        var newS = args.GetValueOrDefault("new_string")?.ToString();
        if (string.IsNullOrEmpty(oldS))
            return "Error: old_string is required.";
        if (newS is null)
            return "Error: new_string is required.";
        PlainTextLineEditor.ReplaceFirst(fullPath, oldS, newS);
        return $"replace_first applied to {Path.GetFileName(fullPath)}.";
    }

    private static string DoReplaceAllRegex(string fullPath, IReadOnlyDictionary<string, object?> args)
    {
        var pattern = args.GetValueOrDefault("pattern")?.ToString();
        var replacement = args.GetValueOrDefault("replacement")?.ToString() ?? "";
        if (string.IsNullOrEmpty(pattern))
            return "Error: pattern is required.";
        PlainTextLineEditor.ReplaceAllRegex(fullPath, pattern, replacement);
        return $"replace_all_regex applied to {Path.GetFileName(fullPath)}.";
    }

    private static string DoInsertLines(string fullPath, IReadOnlyDictionary<string, object?> args)
    {
        var after = ParseInt(args, "after_line", 0, 0, int.MaxValue);
        var lines = LinesOrContent(args);
        if (lines.Count == 0)
            return "Error: lines or new_content is required for insert_lines.";
        PlainTextLineEditor.InsertLinesAfter(fullPath, after, lines);
        return $"Inserted {lines.Count} line(s) after line {after} in {Path.GetFileName(fullPath)}.";
    }

    private static string DoDeleteRange(string fullPath, IReadOnlyDictionary<string, object?> args)
    {
        var s = ParseInt(args, "start_line", 1, 1, int.MaxValue);
        var e = ParseInt(args, "end_line", s, 1, int.MaxValue);
        PlainTextLineEditor.DeleteRange(fullPath, s, e);
        return $"Deleted lines {s}-{e} in {Path.GetFileName(fullPath)}.";
    }

    private static string DoAppendLines(string fullPath, IReadOnlyDictionary<string, object?> args)
    {
        var lines = LinesOrContent(args);
        if (lines.Count == 0)
            return "Error: lines or new_content is required for append_lines.";
        PlainTextLineEditor.AppendLines(fullPath, lines);
        return $"Appended {lines.Count} line(s) to {Path.GetFileName(fullPath)}.";
    }

    private static List<string> GetLinesArg(IReadOnlyDictionary<string, object?> args)
    {
        var list = new List<string>();
        if (!args.TryGetValue("lines", out var val) || val is null)
            return list;

        if (val is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in je.EnumerateArray())
            {
                var s = JsonElementAsString(item);
                if (s is not null)
                    list.Add(s);
            }
        }

        return list;
    }

    private static List<string> LinesOrContent(IReadOnlyDictionary<string, object?> args)
    {
        var lines = GetLinesArg(args);
        if (lines.Count > 0)
            return lines;
        if (args.TryGetValue("new_content", out var nc))
            return PlainTextLineEditor.SplitLines(nc?.ToString()).ToList();
        return lines;
    }
}
