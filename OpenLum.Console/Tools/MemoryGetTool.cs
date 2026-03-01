using System.Text;
using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Tools;

/// <summary>
/// Reads a snippet from MEMORY.md or memory/*.md with optional line range.
/// </summary>
public sealed class MemoryGetTool : ITool
{
    private readonly string _workspaceDir;

    public MemoryGetTool(string workspaceDir)
    {
        _workspaceDir = Path.GetFullPath(workspaceDir);
    }

    public string Name => "memory_get";
    public string Description => "Read a snippet from MEMORY.md or memory/*.md. Use path like 'MEMORY.md' or 'memory/notes.md'. Optional from/lines for line range.";
    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("path", "string", "Relative path: MEMORY.md or memory/*.md", true),
        new ToolParameter("from", "number", "Start line (1-based)", false),
        new ToolParameter("lines", "number", "Number of lines to return", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var path = args.GetValueOrDefault("path")?.ToString();
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult("Error: path is required.");

        var fullPath = ResolvePath(path);
        if (fullPath is null || !File.Exists(fullPath))
            return Task.FromResult($"Error: memory file not found: {path}");

        var from = ReadInt(args, "from", 1);
        var lines = ReadInt(args, "lines", 0);
        if (from < 1) from = 1;
        if (lines < 0) lines = 0;

        var allLines = File.ReadAllLines(fullPath);
        int take, skip;
        if (lines > 0)
        {
            skip = from - 1;
            take = Math.Min(lines, Math.Max(0, allLines.Length - skip));
        }
        else
        {
            skip = from - 1;
            take = Math.Max(0, allLines.Length - skip);
        }

        var sb = new StringBuilder();
        for (var i = 0; i < take && skip + i < allLines.Length; i++)
            sb.AppendLine(allLines[skip + i]);

        var text = sb.ToString();
        var json = "{\"path\":\"" + EscapeJson(path) + "\",\"from\":" + (skip + 1) + ",\"text\":\"" + EscapeJson(text) + "\"}";
        return Task.FromResult(json);
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length + 4);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private string? ResolvePath(string path)
    {
        var trimmed = path.TrimStart('/', '\\').Replace("\\", "/");
        var full = Path.GetFullPath(Path.Combine(_workspaceDir, trimmed));
        if (!full.StartsWith(_workspaceDir, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!trimmed.Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("memory/", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Equals("memory", StringComparison.OrdinalIgnoreCase))
            return null;
        return full;
    }

    private static int ReadInt(IReadOnlyDictionary<string, object?> args, string key, int fallback)
    {
        if (!args.TryGetValue(key, out var v))
            return fallback;
        return v switch
        {
            System.Text.Json.JsonElement je when je.TryGetInt32(out var i) => i,
            long l => (int)l,
            int i => i,
            _ => fallback
        };
    }
}
