namespace OpenLum.Console.Config;

/// <summary>
/// Tool policy configuration: profile + optional allow/deny overrides.
/// Deny wins over allow; profile provides the base allowlist.
/// </summary>
public sealed class ToolPolicyConfig
{
    /// <summary>Base profile: minimal, coding, or full.</summary>
    public string Profile { get; init; } = "coding";

    /// <summary>Additional tools to allow (or restrict to, if profile is applied).</summary>
    public IReadOnlyList<string> Allow { get; init; } = [];

    /// <summary>Tools to deny (takes precedence over allow).</summary>
    public IReadOnlyList<string> Deny { get; init; } = [];
}
