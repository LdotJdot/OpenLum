namespace OpenLum.Console.Config;

/// <summary>
/// Resolves well-known user folders on the host so the model does not need exec/shell
/// to discover Desktop, Documents, etc.
/// </summary>
public static class HostPathHints
{
    /// <summary>
    /// Multi-line block for the system prompt. Empty lines avoided for compactness.
    /// </summary>
    public static string BuildBlock()
    {
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var downloads = Path.Combine(profile, "Downloads");

            var os = OperatingSystem.IsWindows()
                ? $"Windows ({Environment.OSVersion.Version})"
                : OperatingSystem.IsMacOS()
                    ? "macOS"
                    : OperatingSystem.IsLinux()
                        ? "Linux"
                        : Environment.OSVersion.Platform.ToString();

            return
                $"OS: {os}. " +
                $"UserProfile: {profile}. " +
                $"Desktop: {desktop}. " +
                $"Documents: {documents}. " +
                $"Downloads: {downloads}. " +
                "When the user says \"desktop\" or gives a partial path, map it using these values; " +
                "you do not need a shell to discover them.";
        }
        catch
        {
            return "";
        }
    }
}
