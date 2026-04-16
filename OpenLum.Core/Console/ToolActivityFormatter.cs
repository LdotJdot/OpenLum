using System.Text.Json;
using OpenLum.Console.Extractors;
using OpenLum.Console.Tools;

namespace OpenLum.Console.Console;

/// <summary>
/// Human-readable, action-oriented one-liners for console tool logs (muted status lines),
/// instead of raw JSON or shell-style dumps.
/// </summary>
public static class ToolActivityFormatter
{
    public static string FormatStarting(string toolName, string? argsJson)
    {
        return toolName.Trim().ToLowerInvariant() switch
        {
            "grep" => "Grepping…",
            "glob" => "Searching files…",
            "read" => "Reading…",
            "read_many" => "Reading files…",
            "list_dir" => FormatListDirStarting(argsJson),
            "write" => "Writing…",
            "str_replace" => "Editing…",
            "text_edit" => "Editing…",
            "exec" => "Running command…",
            "memory_get" => "Reading memory…",
            "memory_search" => "Searching memory…",
            "todo" => "Updating tasks…",
            "submit_plan" => "Saving plan…",
            "sessions_spawn" => "Running sub-agent…",
            _ => $"{Capitalize(toolName)}…"
        };
    }

    /// <param name="success">False when the tool returned an error result.</param>
    public static string FormatCompleted(string toolName, string? argsJson, bool success)
    {
        if (!success)
            return FormatFailed(toolName, argsJson);

        var doc = TryParseDocument(argsJson);
        try
        {
            var root = doc?.RootElement ?? default;
            return toolName.Trim().ToLowerInvariant() switch
            {
            "grep" => FormatGrepCompleted(root),
            "glob" => FormatGlobCompleted(root),
            "read" => FormatReadCompleted(root),
            "read_many" => FormatReadManyCompleted(root),
            "list_dir" => FormatListDirCompleted(root),
            "write" => FormatWriteCompleted(root),
            "str_replace" => FormatStrReplaceCompleted(root),
            "text_edit" => FormatStrReplaceCompleted(root),
            "exec" => FormatExecCompleted(root),
            "memory_get" => "Read memory entry",
            "memory_search" => "Searched memory",
            "todo" => "Updated task list",
            "submit_plan" => "Saved plan",
            "sessions_spawn" => "Sub-agent finished",
            _ => $"Finished {toolName}"
            };
        }
        finally
        {
            doc?.Dispose();
        }
    }

    private static string FormatFailed(string toolName, string? argsJson)
    {
        using var doc = TryParseDocument(argsJson);
        var hint = doc is null ? null : ShortHint(toolName, doc.RootElement);
        return string.IsNullOrEmpty(hint)
            ? $"Failed: {toolName}"
            : $"Failed: {hint}";
    }

    private static string? ShortHint(string toolName, JsonElement root)
    {
        return toolName.ToLowerInvariant() switch
        {
            "grep" => GetString(root, "pattern"),
            "glob" => GetString(root, "pattern"),
            "read" => ShortPath(GetString(root, "path")),
            "read_many" => null,
            "exec" => Truncate(GetString(root, "command"), 48),
            _ => ShortPath(GetString(root, "path"))
        };
    }

    private static JsonDocument? TryParseDocument(string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson)) return null;
        try
        {
            return JsonDocument.Parse(argsJson);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatGrepCompleted(JsonElement root)
    {
        var pattern = Truncate(GetString(root, "pattern"), 64);
        if (string.IsNullOrEmpty(pattern)) pattern = "(pattern)";

        var pathArg = GetString(root, "path");
        if (string.IsNullOrWhiteSpace(pathArg)) pathArg = ".";

        var glob = GetString(root, "glob");
        var where = DescribePathTarget(pathArg);
        if (!string.IsNullOrEmpty(glob))
            return $"Grepped {pattern} in {where} matching {glob}";
        return $"Grepped {pattern} in {where}";
    }

    private static string FormatGlobCompleted(JsonElement root)
    {
        var pattern = GetString(root, "pattern");
        if (string.IsNullOrEmpty(pattern)) pattern = "*";
        var pathArg = GetString(root, "path");
        if (string.IsNullOrWhiteSpace(pathArg)) pathArg = ".";
        var where = DescribePathTarget(pathArg);
        return $"Searched files {pattern} in {where}";
    }

    private static string FormatReadCompleted(JsonElement root)
    {
        var path = GetString(root, "path");
        var label = string.IsNullOrEmpty(path) ? "file" : ShortPath(path);
        var ext = string.IsNullOrEmpty(path) ? "" : Path.GetExtension(path);
        var isExtract = ExeReadDispatcher.IsSupportedExtractExtension(ext);

        if (isExtract)
        {
            var offset = GetInt(root, "offset", 0);
            var limit = GetInt(root, "limit", 2000);
            if (offset == 0 && limit >= 2000)
                return $"Read {label}";
            var lim = Math.Max(1, limit);
            var endInclusive = offset + lim - 1;
            return $"Read {label} chars {offset}–{endInclusive}";
        }

        var lineOffset = GetInt(root, "offset", 1);
        var lineLimit = GetInt(root, "limit", 200);
        if (lineOffset == 1 && lineLimit >= 2000)
            return $"Read {label}";
        var endLine = lineOffset + Math.Max(0, lineLimit - 1);
        return $"Read {label} L{lineOffset}-{endLine}";
    }

    private static string FormatReadManyCompleted(JsonElement root)
    {
        var n = 0;
        if (root.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array)
            n = paths.GetArrayLength();
        else if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            n = items.GetArrayLength();
        return n > 0 ? $"Read {n} files" : "Read multiple files";
    }

    private static string FormatListDirStarting(string? argsJson)
    {
        var doc = TryParseDocument(argsJson);
        if (doc is null) return "Listing directory…";
        using (doc)
        {
            if (doc.RootElement.TryGetProperty("recursive", out var r) && r.ValueKind == JsonValueKind.True)
                return "Listing directory tree…";
            var glob = GetString(doc.RootElement, "name_glob");
            if (!string.IsNullOrWhiteSpace(glob))
                return "Listing directory (filtered)…";
            return "Listing directory…";
        }
    }

    private static string FormatListDirCompleted(JsonElement root)
    {
        var path = GetString(root, "path");
        if (string.IsNullOrWhiteSpace(path)) path = ".";
        var where = DescribePathTarget(path);
        var glob = GetString(root, "name_glob");
        var globHint = string.IsNullOrWhiteSpace(glob) ? null : Truncate(glob.Trim(), 40);
        if (root.TryGetProperty("recursive", out var r) && r.ValueKind == JsonValueKind.True)
            return $"Listed tree under {where}";
        if (!string.IsNullOrEmpty(globHint))
            return $"Listed {where} ({globHint})";
        return $"Listed {where}";
    }

    private static string FormatWriteCompleted(JsonElement root)
    {
        var path = GetString(root, "path");
        return string.IsNullOrEmpty(path) ? "Wrote file" : $"Wrote {ShortPath(path)}";
    }

    private static string FormatStrReplaceCompleted(JsonElement root)
    {
        var path = GetString(root, "path");
        return string.IsNullOrEmpty(path) ? "Edited file" : $"Edited {ShortPath(path)}";
    }

    private static string FormatExecCompleted(JsonElement root)
    {
        var cmd = GetString(root, "command");
        if (string.IsNullOrEmpty(cmd)) return "Ran command";
        var oneLine = cmd.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return $"Ran {Truncate(oneLine, 72)}";
    }

    private static string DescribePathTarget(string path)
    {
        path = path.Trim().Replace('\\', '/');
        if (path is "." or "./") return "workspace";
        try
        {
            var full = Path.GetFullPath(path);
            var name = Path.GetFileName(full.TrimEnd('/', '\\'));
            if (!string.IsNullOrEmpty(name) && name != "/" && name != "\\")
                return name;
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                return Path.GetFileName(dir) ?? path;
        }
        catch { /* ignore */ }

        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 ? segments[^1] : path;
    }

    private static string ShortPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "file";
        path = path.Replace('\\', '/');
        if (path.Length <= 56) return path;
        var name = Path.GetFileName(path);
        return string.IsNullOrEmpty(name) ? path : $"…/{name}";
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            _ => p.ToString()
        };
    }

    private static int GetInt(JsonElement el, string name, int defaultVal)
    {
        if (!el.TryGetProperty(name, out var p)) return defaultVal;
        return ToolArgHelpers.TryParseJsonElementInt(p, out var i) ? i : defaultVal;
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    private static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s[1..];
    }
}
