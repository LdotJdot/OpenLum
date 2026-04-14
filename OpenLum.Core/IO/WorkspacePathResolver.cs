namespace OpenLum.Console.IO;

/// <summary>
/// Result of resolving a user-supplied path against the workspace root.
/// </summary>
public readonly record struct PathResolution
{
    public string? FullPath { get; init; }
    public string? Error { get; init; }
    public bool IsOk => FullPath is not null;

    public static PathResolution Ok(string fullPath) => new() { FullPath = fullPath };
    public static PathResolution Err(string message) => new() { Error = message };
}

/// <summary>
/// Resolves user-supplied paths (relative or absolute) against a workspace root.
/// All Tier-1 tools share this single resolver to guarantee consistent behavior.
///
/// Design:
///   - Relative paths resolve against workspace root.
///   - Absolute paths (including ~ expansion) are accepted as-is.
///   - No whitelist restriction: the agent runs with user permissions on
///     the user's machine, not in a sandbox. When the user provides an
///     explicit absolute path (e.g. "D:\Desktop\fly报奖\总结.md"), that is
///     a clear instruction and must be honored without round-tripping.
/// </summary>
public sealed class WorkspacePathResolver
{
    private readonly string _workspaceRoot;

    public WorkspacePathResolver(string workspaceRoot, IReadOnlyList<string>? extraAllowedRoots = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public string WorkspaceRoot => _workspaceRoot;

    /// <summary>
    /// Resolve a user-supplied path to a canonicalized full path.
    /// Relative → based on workspace. Absolute / ~ → used directly.
    /// </summary>
    public PathResolution Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return PathResolution.Err("Error: path is required.");

        var expanded = ExpandHome(path.Trim());

        var fullPath = Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(_workspaceRoot, expanded.TrimStart('/', '\\')));

        return PathResolution.Ok(fullPath);
    }

    /// <summary>
    /// Resolve and verify the file exists.
    /// </summary>
    public PathResolution ResolveExistingFile(string? path)
    {
        var r = Resolve(path);
        if (!r.IsOk) return r;
        return File.Exists(r.FullPath!)
            ? r
            : PathResolution.Err($"Error: file not found: {path} (resolved to {r.FullPath})");
    }

    /// <summary>
    /// Resolve and verify the directory exists.
    /// </summary>
    public PathResolution ResolveExistingDirectory(string? path)
    {
        var r = Resolve(path);
        if (!r.IsOk) return r;
        return Directory.Exists(r.FullPath!)
            ? r
            : PathResolution.Err($"Error: directory not found: {path} (resolved to {r.FullPath})");
    }

    private static string ExpandHome(string path)
    {
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
                return Path.Combine(home, path[2..]);
        }
        return path;
    }
}
