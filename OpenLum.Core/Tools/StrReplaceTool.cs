using OpenLum.Console.Editing;
using OpenLum.Console.Interfaces;
using OpenLum.Console.IO;
using static OpenLum.Console.Tools.ToolArgHelpers;

namespace OpenLum.Console.Tools;

/// <summary>
/// Performs exact string replacement in a file — safer than full-file write for small edits.
/// Mirrors the StrReplace pattern: old_string must be unique (unless replace_all is true).
/// </summary>
public sealed class StrReplaceTool : ITool
{
    private readonly WorkspacePathResolver _resolver;

    public StrReplaceTool(WorkspacePathResolver resolver) => _resolver = resolver;

    public StrReplaceTool(string workspaceDir) : this(new WorkspacePathResolver(workspaceDir)) { }

    public string Name => "str_replace";

    public string Description =>
        "Replace exact text in a file. old_string must uniquely match one location " +
        "(or set replace_all=true to replace every occurrence). " +
        "Prefer this over write for small edits. Call read on the file (or the slice) first so old_string matches the file literally—use real < > & characters, not XML-style spellings or six-character sequences like backslash-u-0-0-3-c for '<'. " +
        "For long blocks or whole-function edits, use text_edit: read_range to get line numbers, then replace_range — avoids huge old_string mismatches.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("path", "string", "File path (workspace-relative or absolute).", true),
        new ToolParameter("old_string", "string",
            "Exact text to find (must match file content exactly, including whitespace). Copy from a fresh read—avoid spelling < as backslash-u-0-0-3-c or similar when the file uses the real character.", true),
        new ToolParameter("new_string", "string", "Replacement text (must differ from old_string).", true),
        new ToolParameter("replace_all", "boolean", "If true, replace all occurrences (default false).", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var pathArg = args.GetValueOrDefault("path")?.ToString()?.Trim();
        var oldStr = args.GetValueOrDefault("old_string")?.ToString();
        var newStr = args.GetValueOrDefault("new_string")?.ToString();
        var replaceAll = ParseBool(args, "replace_all");

        var res = _resolver.ResolveExistingFile(pathArg);
        if (!res.IsOk)
            return Task.FromResult(res.Error!);
        if (string.IsNullOrEmpty(oldStr))
            return Task.FromResult("Error: old_string is required and must not be empty.");
        if (newStr is null)
            return Task.FromResult("Error: new_string is required.");
        if (oldStr == newStr)
            return Task.FromResult("Error: old_string and new_string are identical — nothing to replace.");

        var fullPath = res.FullPath!;

        string content;
        try
        {
            content = File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error reading file: {ex.Message}");
        }

        var effectiveOld = TextMatchHelpers.ResolveOldString(content, oldStr);
        if (effectiveOld is null)
        {
            var extra = LooksLikeUnicodeEscapeLiteral(oldStr)
                ? " Your old_string looks like a JSON/Unicode escape spelling (e.g. six characters backslash-u-0-0-3-c) while the file may use the real symbol—re-read the file and paste the exact bytes, or use text_edit read_range → replace_range. "
                : " ";
            return Task.FromResult(
                "Error: old_string not found in file. Make sure it matches the file content exactly (including whitespace and indentation)." + extra +
                "If the block is long or spans many lines, prefer text_edit: operation read_range (get 1-based line numbers), then replace_range with new_content. " +
                "Also check CRLF vs LF if you pasted from another view.");
        }

        var count = TextMatchHelpers.CountOccurrences(content, effectiveOld);
        if (count > 1 && !replaceAll)
            return Task.FromResult(
                $"Error: old_string matches {count} locations. Provide more surrounding context to make it unique, or set replace_all=true.");

        var updated = replaceAll
            ? content.Replace(effectiveOld, newStr)
            : ReplaceFirstSegment(content, effectiveOld, newStr);

        try
        {
            File.WriteAllText(fullPath, updated);
            var noun = replaceAll ? $"{count} occurrence(s)" : "1 occurrence";
            return Task.FromResult($"Replaced {noun} in {pathArg}.");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error writing file: {ex.Message}");
        }
    }

    private static bool LooksLikeUnicodeEscapeLiteral(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 6) return false;
        for (var i = 0; i + 5 < s.Length; i++)
        {
            if (s[i] != '\\' || (s[i + 1] != 'u' && s[i + 1] != 'U')) continue;
            if (IsHex(s[i + 2]) && IsHex(s[i + 3]) && IsHex(s[i + 4]) && IsHex(s[i + 5]))
                return true;
        }

        return false;
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static string ReplaceFirstSegment(string text, string oldValue, string newValue)
    {
        var idx = text.IndexOf(oldValue, StringComparison.Ordinal);
        if (idx < 0) return text;
        return string.Concat(text.AsSpan(0, idx), newValue, text.AsSpan(idx + oldValue.Length));
    }
}
