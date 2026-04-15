namespace OpenLum.Console.Config;

/// <summary>
/// Resolves the host bundle root: the folder that contains both Skills and InternalTools.
/// Precedence: <c>OPENLUM_HOST_ROOT</c> env → <see cref="AppConfig.HostRoot"/> → <see cref="AppContext.BaseDirectory"/>.
/// </summary>
public static class HostPathResolver
{
    /// <summary>Environment variable name for overriding the host root directory.</summary>
    public const string EnvHostRoot = "OPENLUM_HOST_ROOT";

    public static string ResolveHostRoot(string? configHostRoot)
    {
        var env = Environment.GetEnvironmentVariable(EnvHostRoot);
        var raw = !string.IsNullOrWhiteSpace(env) ? env.Trim() : configHostRoot;
        if (!string.IsNullOrWhiteSpace(raw))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim()));
        return Path.GetFullPath(AppContext.BaseDirectory);
    }
}
