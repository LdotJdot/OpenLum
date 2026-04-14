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
        "Prefer this over write for small edits to avoid overwriting the whole file.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("path", "string", "File path (workspace-relative or absolute).", true),
        new ToolParameter("old_string", "string", "Exact text to find (must match file content exactly, including whitespace).", true),
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

        var count = CountOccurrences(content, oldStr);
        if (count == 0)
            return Task.FromResult(
                "Error: old_string not found in file. Make sure it matches the file content exactly (including whitespace and indentation).");

        if (count > 1 && !replaceAll)
            return Task.FromResult(
                $"Error: old_string matches {count} locations. Provide more surrounding context to make it unique, or set replace_all=true.");

        var updated = replaceAll
            ? content.Replace(oldStr, newStr)
            : ReplaceFirst(content, oldStr, newStr);

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

    private static int CountOccurrences(string text, string search)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(search, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += search.Length;
        }
        return count;
    }

    private static string ReplaceFirst(string text, string oldValue, string newValue)
    {
        var idx = text.IndexOf(oldValue, StringComparison.Ordinal);
        if (idx < 0) return text;
        return string.Concat(text.AsSpan(0, idx), newValue, text.AsSpan(idx + oldValue.Length));
    }

}
