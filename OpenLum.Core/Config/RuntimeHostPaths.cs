namespace OpenLum.Console.Config;

/// <summary>
/// Runtime paths for the OpenLum host bundle: a single root directory that contains
/// <c>skills/</c> (or <c>Skills/</c>) and <c>InternalTools/</c>, separate from the user workspace.
/// Set <see cref="HostRoot"/> once at startup before using extractors or skill loading.
/// </summary>
public static class RuntimeHostPaths
{
    private static string _hostRoot = Path.GetFullPath(AppContext.BaseDirectory);

    /// <summary>Directory that contains <c>skills</c> and <c>InternalTools</c> subfolders.</summary>
    public static string HostRoot
    {
        get => _hostRoot;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Host root cannot be empty.", nameof(value));
            _hostRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim()));
        }
    }

    /// <summary><c>InternalTools</c> under <see cref="HostRoot"/>.</summary>
    public static string InternalToolsRoot => Path.Combine(HostRoot, "InternalTools");

    /// <summary>
    /// The skills container directory under <see cref="HostRoot"/> if it exists (<c>skills</c> or <c>Skills</c>), otherwise null.
    /// </summary>
    public static string? TryGetSkillsContainerPath()
    {
        var lower = Path.Combine(HostRoot, "skills");
        var upper = Path.Combine(HostRoot, "Skills");
        if (Directory.Exists(lower))
            return lower;
        if (Directory.Exists(upper))
            return upper;
        return null;
    }
}
