namespace OpenLum.Console.Config;

/// <summary>
/// Defines tool groups and profiles for policy resolution.
/// </summary>
public static class ToolProfiles
{
    /// <summary>Tool groups: group name -> tool names.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Groups { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["group:fs"] = ["read", "read_many", "write", "str_replace", "text_edit", "list_dir", "todo", "submit_plan"],
            ["group:search"] = ["grep", "glob"],
            ["group:web"] = ["browser_navigate", "browser_snapshot", "browser_tabs", "browser_click", "browser_type", "browser_page_text", "browser_screenshot", "browser_upload"],
            ["group:runtime"] = ["exec"],
            ["group:memory"] = ["memory_get", "memory_search"],
            ["group:sessions"] = ["sessions_spawn"]
        };

    /// <summary>Profiles: profile name -> base allowlist (group or tool names).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ProfileAllowlists { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["minimal"] = ["list_dir"],
            ["coding"] = ["group:fs", "group:search", "group:web", "group:runtime"],
            ["messaging"] = ["list_dir"],
            ["local"] = ["group:fs", "group:search", "group:web", "group:runtime", "group:memory", "group:sessions"],
            ["full"] = ["group:fs", "group:search", "group:web", "group:runtime", "group:memory", "group:sessions"]
        };

    /// <summary>Expands group names to concrete tool names.</summary>
    public static IReadOnlyList<string> Expand(IEnumerable<string> names)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var n = Normalize(name);
            if (Groups.TryGetValue(n, out var tools))
            {
                foreach (var t in tools)
                    result.Add(t);
            }
            else
            {
                result.Add(n);
            }
        }
        return result.ToList();
    }

    public static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
