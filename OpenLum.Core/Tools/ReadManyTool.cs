using System.Text;
using System.Text.Json;
using OpenLum.Console.Interfaces;
using OpenLum.Console.IO;
using static OpenLum.Console.Tools.ToolArgHelpers;

namespace OpenLum.Console.Tools;

/// <summary>
/// Reads multiple plain-text files in one tool call to reduce round trips.
/// Each item can specify its own offset/limit; defaults match ReadTool.
/// </summary>
public sealed class ReadManyTool : ITool
{
    private readonly WorkspacePathResolver _resolver;

    public ReadManyTool(WorkspacePathResolver resolver) => _resolver = resolver;

    public string Name => "read_many";

    public string Description =>
        "Read multiple **plain-text** files in one call (line windows; no extractor pipeline). Output is per-file blocks—do not assume the same lines match across different files. " +
        "For **folder-wide Markdown/text review**, after **glob** lists paths, use **read_many** in the same turn when possible instead of many serial **read** calls. " +
        "PDF/Office/CAD and similar types are handled by the single-file **read** tool (host extractors, often subprocess-based). This batch tool does not invoke those extractors yet—so use **read** per file for those extensions today (parallel batching could be added later without changing that fact). " +
        "Provide items=[{path, offset?, limit?}] or paths=[...]. " +
        "If the call errors, fix paths/JSON and retry once—do not fall back to reading every file in a tree unless the user asked for that. " +
        "Per file, **offset** and **limit** are numbers; next window starts at **offset + limit** (no overlap). " +
        "Each file returns numbered lines in this tool's standard format.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("items", "array", "Array of {path, offset?, limit?} objects.", false),
        new ToolParameter("paths", "array", "Array of file paths (uses shared offset/limit).", false),
        new ToolParameter("offset", "number", "Numeric window start for all paths (default 1). Next chunk: offset + limit. Negative: count from end.", false),
        new ToolParameter("limit", "number", "Numeric window size per file (default 200, max 2000). Next chunk: offset + limit.", false),
        new ToolParameter("max_total_lines", "number", "Max total lines across all files (default 4000, max 20000).", false),
        new ToolParameter("max_bytes_per_file", "number", "Skip files larger than this many bytes (default 5242880 = 5MB; aligns with read tool safety).", false)
    ];

    private const long DefaultMaxBytesPerFile = 5 * 1024 * 1024;

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var maxTotalLines = ParseInt(args, "max_total_lines", 4000, 1, 20000, zeroMeansDefault: true);
        var maxBytesPerFile = (long)ParseInt(args, "max_bytes_per_file", (int)DefaultMaxBytesPerFile, 1, int.MaxValue, zeroMeansDefault: true);
        var defaultLimit = ParseInt(args, "limit", 200, 1, 2000, zeroMeansDefault: true);
        var defaultOffset = ParseInt(args, "offset", 1);

        var requests = ParseRequests(args, defaultOffset, defaultLimit);
        if (requests.Count == 0)
            return Task.FromResult("Error: provide items=[{path,...}] or paths=[...].");

        var sb = new StringBuilder();
        var remainingBudget = maxTotalLines;

        for (var idx = 0; idx < requests.Count; idx++)
        {
            if (ct.IsCancellationRequested) break;
            if (remainingBudget <= 0)
            {
                sb.AppendLine("... [max_total_lines reached]");
                break;
            }

            var r = requests[idx];
            if (idx > 0) sb.AppendLine();
            sb.AppendLine($"== {r.Path} ==");

            var res = _resolver.ResolveExistingFile(r.Path);
            if (!res.IsOk)
            {
                sb.AppendLine(res.Error);
                continue;
            }

            var full = res.FullPath!;
            try
            {
                var len = new FileInfo(full).Length;
                if (len > maxBytesPerFile)
                {
                    sb.AppendLine(
                        $"Skipped: file too large ({len} bytes > max_bytes_per_file={maxBytesPerFile}). Use grep/read with offset/limit on this path, or raise max_bytes_per_file.");
                    continue;
                }
            }
            catch
            {
                // proceed; ReadAllLines may still work
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(full);
            }
            catch (Exception ex)
            {
                sb.AppendLine(
                    $"Error reading file: {ex.Message}. Extracted/binary formats need the **read** tool; **read_many** only reads plain text here (no bundled extractors in this path).");
                continue;
            }

            var offset = r.Offset;
            var limit = Math.Min(r.Limit, remainingBudget);

            int startIdx;
            if (offset < 0)
                startIdx = Math.Max(0, lines.Length + offset);
            else
                startIdx = Math.Max(0, offset - 1);

            var take = Math.Min(lines.Length - startIdx, limit);
            if (take <= 0)
            {
                sb.AppendLine($"(file has {lines.Length} lines; requested range is empty)");
                continue;
            }

            for (var i = startIdx; i < startIdx + take; i++)
                sb.AppendLine($"{i + 1,6}|{lines[i]}");

            remainingBudget -= take;

            if (startIdx + take < lines.Length)
                sb.AppendLine($"... [{lines.Length - startIdx - take} more lines, {lines.Length} total]");
        }

        return Task.FromResult(sb.ToString());
    }

    private static List<ReadReq> ParseRequests(
        IReadOnlyDictionary<string, object?> args,
        int defaultOffset,
        int defaultLimit)
    {
        var list = new List<ReadReq>();

        if (args.TryGetValue("items", out var itemsVal) && itemsVal is JsonElement itemsJe &&
            itemsJe.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in itemsJe.EnumerateArray())
            {
                if (elem.ValueKind != JsonValueKind.Object) continue;
                var path = elem.TryGetProperty("path", out var p) ? p.GetString() : null;
                if (string.IsNullOrWhiteSpace(path)) continue;

                var offset = defaultOffset;
                if (elem.TryGetProperty("offset", out var o) && ToolArgHelpers.TryParseJsonElementInt(o, out var oi)) offset = oi;
                var limit = defaultLimit;
                if (elem.TryGetProperty("limit", out var l) && ToolArgHelpers.TryParseJsonElementInt(l, out var li)) limit = li;
                limit = Math.Clamp(limit, 1, 2000);

                list.Add(new ReadReq(path.Trim(), offset, limit));
            }

            return list;
        }

        if (args.TryGetValue("paths", out var pathsVal) && pathsVal is JsonElement pathsJe &&
            pathsJe.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in pathsJe.EnumerateArray())
            {
                var path = elem.ValueKind == JsonValueKind.String ? elem.GetString() : null;
                if (string.IsNullOrWhiteSpace(path)) continue;
                list.Add(new ReadReq(path.Trim(), defaultOffset, defaultLimit));
            }
        }

        return list;
    }

    private readonly record struct ReadReq(string Path, int Offset, int Limit);
}

