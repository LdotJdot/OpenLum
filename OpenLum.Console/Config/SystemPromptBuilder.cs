using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Config;

/// <summary>
/// Builds the system prompt from workspace, tools (filtered by policy), and skills.
/// Keeps high-level methodology here; tool-specific usage lives on each tool's description.
/// </summary>
public static class SystemPromptBuilder
{
    /// <summary>Tool order for prompt. Search/read/edit tools first, then runtime/memory/sessions.</summary>
    private static readonly string[] ToolOrder =
    [
        "semantic_search", "grep", "glob", "read", "read_many", "write", "str_replace", "list_dir",
        "todo", "submit_plan", "exec",
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
            "## Execution process",
            "For trivial, one-step questions, answer directly and concisely.",
            "For non-trivial work: (1) **Intent** — what the user wants and what would count as success; do not widen scope on your own after a setback. (2) **Constraints** — context, risk, missing info. (3) **Solve** — tools or direct answer.",
            "",
            dateLine,
            "",
            "## Workflow (built-in)",
            "The runtime uses **Observe → Act → Verify** by default. A dynamic `[Workflow]` line in context names the current phase. **Observe** is for gathering context with read-only tools first; shell and edits come when the phase allows. How to unlock the next phase is described in the **submit_plan** and **todo** tools (when the host requires a plan before writes).",
            "",
            "## Goal anchoring",
            "Keep a clear **task contract**: what was asked and what counts as done. Do not silently change the goal after an empty result (no unrelated fishing or redefining the task). For lookup-style requests, a one-line `<thinking>` restating the target can reduce drift.",
            "",
            "## Tools",
            toolLines.Count > 0 ? string.Join("\n", toolLines) : "(no tools)",
            "",
            "## Output",
            "By default answer concisely without visible reasoning. For complex or high-impact tasks, you may wrap planning in `<thinking>...</thinking>`; then reply or call tools.",
            "",
            "## Tool call style",
            "Minimize redundant calls. Prefer batching read-only work as supported by the tools (see descriptions).",
            "",
            "## Task management",
            "Use **todo** / **submit_plan** for multi-step work; skip for one-shot tasks. Semantics are defined on those tools.",
            "",
            "## Safety",
            "Prioritize safety and human oversight. If instructions conflict, pause and ask.",
            ""
        };

        lines.Add("## Workspace");
        lines.Add($"Workspace: {workspaceDir}");
        lines.Add("Paths may be workspace-relative or absolute per tool behavior.");
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
