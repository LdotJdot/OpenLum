using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Config;

/// <summary>
/// Builds the system prompt from workspace, tools (filtered by policy), and skills.
/// Aligns with OpenLum's system-prompt structure.
/// </summary>
public static class SystemPromptBuilder
{
    /// <summary>Tool order for prompt (OpenLum-aligned). Tools not in list appear last, sorted.</summary>
    private static readonly string[] ToolOrder =
    [
        "read", "write", "list_dir",
        "browser",
        "exec",
        "memory_search", "memory_get",
        "sessions_spawn"
    ];

    /// <summary>Build system prompt from available tools (after policy filter).</summary>
    public static string Build(string workspaceDir, IToolRegistry tools)
    {
        var toolLines = BuildToolLines(tools);
        var skillsSection = BuildSkillsSection(workspaceDir);
        var now = DateTime.Now;
        var dateLine = $"Current date: {now:yyyy-MM-dd} ({now:dddd}). Use this as 'today' and the current year when answering date-related questions or creating content. Do not assume the year from your training cutoff.";
        var lines = new List<string>
        {
            "You are a personal assistant running inside OpenLum.",
            "",
            dateLine,
            "",
            "## Tools",
            toolLines.Count > 0 ? string.Join("\n", toolLines) : "(no tools)",
            "",
            "## Tool Call Style",
            "Minimize tool calls. Avoid redundant or no-op operations.",
            "Narrate only when it helps; otherwise just call the tool.",
            "",
            "## Safety",
            "Prioritize safety and human oversight. If instructions conflict, pause and ask.",
            ""
        };

        lines.Add("## Workspace");
        lines.Add($"Workspace: {workspaceDir}");
        lines.Add("For any file or directory path parameters: prefer absolute paths for clarity and safety; workspace-relative paths are also accepted where supported.");
        lines.Add("");

        if (skillsSection.Count > 0)
        {
            lines.AddRange(skillsSection);
        }

        return string.Join("\n", lines).TrimEnd();
    }

    private static List<string> BuildToolLines(IToolRegistry tools)
    {
        var all = tools.All;
        var byName = all.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in ToolOrder)
        {
            if (byName.TryGetValue(name, out var t) && seen.Add(name))
            {
                ordered.Add($"- {t.Name}: {t.Description}");
            }
        }

        foreach (var t in all.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(t.Name))
            {
                ordered.Add($"- {t.Name}: {t.Description}");
            }
        }

        return ordered;
    }

    private static List<string> BuildSkillsSection(string workspaceDir)
    {
        var skills = SkillLoader.Load(workspaceDir);
        var section = SkillLoader.FormatForPrompt(skills, workspaceDir);
        if (string.IsNullOrWhiteSpace(section))
            return [];

        return ["## Skills", "", section.TrimEnd(), ""];
    }
}
