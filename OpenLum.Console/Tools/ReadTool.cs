using System.Text;
using System.Text.Json;
using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Tools;

/// <summary>
/// Reads plain text file contents from the workspace. For Office/PDF/CAD files, use the read skill exes via exec (read-docx.exe, read-pdf.exe, etc.).
/// Also allows reading from skill directories (for loading SKILL.md on demand).
/// </summary>
public sealed class ReadTool : ITool
{
    private readonly string _workspaceDir;
    private readonly string[] _extraReadRoots;

    public ReadTool(string workspaceDir, IReadOnlyList<string>? extraReadRoots = null)
    {
        _workspaceDir = Path.GetFullPath(workspaceDir);
        _extraReadRoots = extraReadRoots != null
            ? extraReadRoots.Select(Path.GetFullPath).Where(Directory.Exists).ToArray()
            : [];
    }

    public string Name => "read";
    public string Description => "PowerShell-style: like Get-Content. Read plain text (txt, md, json). For PDF/Office/CAD: exec skills/read/<format>/read-<format>.exe path --start 0 --limit N.";
    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("path", "string", "Path (workspace-relative or absolute; supports ~). Like Get-Content -Path.", true),
        new ToolParameter("limit", "number", "Max lines (default 200, max 2000). Like Get-Content -TotalCount.", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var path = args.GetValueOrDefault("path")?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult("Error: path is required.");

        var expandedPath = ExpandPath(path);
        var fullPath = Path.IsPathRooted(expandedPath)
            ? Path.GetFullPath(expandedPath)
            : Path.GetFullPath(Path.Combine(_workspaceDir, expandedPath.TrimStart('/', '\\')));
        if (!IsPathAllowed(fullPath))
            return Task.FromResult($"Error: path is outside workspace or skill directories ({_workspaceDir}).");

        if (!File.Exists(fullPath))
            return Task.FromResult($"Error: file not found: {path}");

        var limit = 200;
        if (args.TryGetValue("limit", out var limitVal))
        {
            var n = limitVal switch
            {
                JsonElement je when je.TryGetInt32(out var i) => i,
                long l => (int)l,
                int i => i,
                _ => 0
            };
            if (n > 0) limit = Math.Min(n, 2000);
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(fullPath);
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error reading file: {ex.Message}. For Office/PDF/CAD, use exec with read-docx.exe, read-pdf.exe etc. from Skills/read.");
        }

        if (IsSkillFile(fullPath))
        {
            var skillName = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? "skill";
            LogSkillLoaded(skillName, fullPath);
        }

        var take = Math.Min(lines.Length, limit);
        var sb = new StringBuilder();
        for (var i = 0; i < take; i++)
            sb.AppendLine(lines[i]);
        if (lines.Length > limit)
            sb.AppendLine($"... [{lines.Length - limit} more lines]");

        return Task.FromResult(sb.ToString());
    }

    private static string ExpandPath(string path)
    {
        if (path.Length == 0) return path;
        if (path == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? path;
        if ((path.StartsWith("~/") || path.StartsWith("~\\")))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
                return Path.Combine(home, path[2..].Replace('/', Path.DirectorySeparatorChar));
        }
        return path;
    }

    private bool IsPathAllowed(string fullPath)
    {
        if (fullPath.StartsWith(_workspaceDir, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var root in _extraReadRoots)
        {
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
