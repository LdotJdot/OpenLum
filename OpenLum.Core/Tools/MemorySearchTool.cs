using System.Text;
using OpenLum.Console.Interfaces;
using static OpenLum.Console.Tools.ToolArgHelpers;

namespace OpenLum.Console.Tools;

/// <summary>
/// Keyword search over MEMORY.md and memory/*.md (simplified; no embeddings).
/// </summary>
public sealed class MemorySearchTool : ITool
{
    private readonly string _workspaceDir;

    public MemorySearchTool(string workspaceDir)
    {
        _workspaceDir = Path.GetFullPath(workspaceDir);
    }

    public string Name => "memory_search";
    public string Description =>
        "Search MEMORY.md and memory/*.md for lines containing ALL space-separated query terms (case-insensitive). " +
        "Returns JSON {results:[{path,startLine,endLine,snippet},...]}. If results is empty, no memory files matched or terms were too strict. " +
        "Before answering about prior work, decisions, or preferences, search memory first.";
    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("query", "string", "Search query (keyword match)", true),
        new ToolParameter("maxResults", "number", "Max snippets to return (default 10)", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var query = args.GetValueOrDefault("query")?.ToString();
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult("Error: query is required.");

        var maxResults = 10;
        if (args.TryGetValue("maxResults", out var v) && TryParseIntObject(v, out var n) && n > 0)
            maxResults = Math.Min(n, 50);

        var files = GetMemoryFiles();
        var sb = new StringBuilder();
        sb.Append("{\"results\":[");
        var first = true;
        var count = 0;
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .ToList();
        if (terms.Count == 0)
            terms = [query.Trim().ToLowerInvariant()];

        foreach (var (relPath, fullPath) in files)
        {
            if (count >= maxResults) break;
            var lines = File.ReadAllLines(fullPath);
            for (var i = 0; i < lines.Length && count < maxResults; i++)
            {
                var line = lines[i];
                var lower = line.ToLowerInvariant();
                if (terms.All(t => lower.Contains(t)))
                {
                    if (!first) sb.Append(',');
                    var snippet = line.Length > 300 ? line[..300] + "..." : line;
                    sb.Append("{\"path\":\"").Append(EscapeJson(relPath)).Append("\"");
                    sb.Append(",\"startLine\":").Append(i + 1);
                    sb.Append(",\"endLine\":").Append(i + 1);
                    sb.Append(",\"snippet\":\"").Append(EscapeJson(snippet)).Append("\"}");
                    first = false;
                    count++;
                }
            }
        }
        sb.Append("]}");
        return Task.FromResult(sb.ToString());
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

    private List<(string RelPath, string FullPath)> GetMemoryFiles()
    {
        var list = new List<(string, string)>();
        var memPath = Path.Combine(_workspaceDir, "MEMORY.md");
        if (File.Exists(memPath))
            list.Add(("MEMORY.md", memPath));

        var memDir = Path.Combine(_workspaceDir, "memory");
        if (Directory.Exists(memDir))
        {
            foreach (var f in Directory.GetFiles(memDir, "*.md"))
            {
                var rel = "memory/" + Path.GetFileName(f);
                list.Add((rel, f));
            }
        }

        return list;
    }
}
