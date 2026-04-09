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
        new ToolParameter("limit", "number", "Max lines to return (default 200, max 2000).", false)
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

        string[] lines;
        try
        {
            lines = File.ReadAllLines(fullPath);
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                $"Error reading file: {ex.Message}. For Office/PDF/CAD, use exec with the corresponding skill exe.");
        }

        if (IsSkillFile(fullPath))
        {
            var skillName = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? "skill";
            LogSkillLoaded(skillName, fullPath);
        }

        int startIdx;
        if (offset < 0)
            startIdx = Math.Max(0, lines.Length + offset);
        else
            startIdx = Math.Max(0, offset - 1);

        var take = Math.Min(lines.Length - startIdx, limit);
        if (take <= 0)
            return Task.FromResult($"(file has {lines.Length} lines; requested range is empty)");

        var sb = new StringBuilder();
        for (var i = startIdx; i < startIdx + take; i++)
            sb.AppendLine($"{i + 1,6}|{lines[i]}");

        if (startIdx + take < lines.Length)
            sb.AppendLine($"... [{lines.Length - startIdx - take} more lines, {lines.Length} total]");

        return Task.FromResult(sb.ToString());
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
}
