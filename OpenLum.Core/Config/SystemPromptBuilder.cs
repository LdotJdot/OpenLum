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
        "grep", "glob", "read", "read_many", "write", "str_replace", "text_edit", "list_dir",
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
            "The runtime uses **Observe → (optional ActPrep) → Act → Verify** by default when workflow is enabled. A dynamic `[Workflow]` line names the current phase. **Observe** favors read-only actions first. **ActPrep** (if enabled by the host) is a single no-tool round for `<thinking>` before **Act**. How **Act** is unlocked is defined on the planning-related entries in **Tools** (when the host requires a plan before writes).",
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
            "Minimize redundant calls. Prefer batching read-only work as supported by the tools (see descriptions). " +
            "Each tool's own description defines what it handles; pick the tool that matches the task and path. " +
            "Optional **Skills** metadata below is not a substitute for those descriptions—use it only when a task clearly depends on a listed skill.",
            "",
            "## Efficiency (tokens & turns)",
            "Principles only—parameters, names, and edge cases live in each entry under **Tools** above.",
            "- Prefer the smallest scope that still answers the question: avoid broad discovery when the user already pinned a location or when a narrow, targeted pass is enough.",
            "- When listing directory contents, apply filename filtering when available instead of expanding everything.",
            "- Plain text vs extracted/binary handling and how “windows” of content are measured differ by tool; follow the relevant description in **Tools**.",
            "- Several independent read-only steps that do not depend on each other’s *interpreted* output should be issued in **one assistant turn** when the API allows, so the host can run them concurrently.",
            "- Many files in the same area that need the same kind of pass: prefer one batched or multi-target operation from **Tools** over one file per turn across many rounds.",
            "- If step B only needs step A’s output as structured input and you do not need to read or judge A’s text yourself, combine those steps in one assistant turn when possible; use separate turns when you must inspect intermediate output.",
            "",
            "## Task management",
            "For multi-step work, use the planning-related tools listed under **Tools**; skip for one-shot tasks. Semantics are on those entries.",
            "",
            "## Safety",
            "Prioritize safety and human oversight. If instructions conflict, pause and ask.",
            ""
        };

        if (RegistryHasTool(tools, "sessions_spawn"))
        {
            var taskIdx = lines.IndexOf("## Task management");
            if (taskIdx >= 0)
            {
                lines.Insert(taskIdx,
                    "- Delegation / sub-session capabilities (see **Tools**) are for isolation or long-running forked work—not for repeating the same read-only exploration you can do in this session.");
            }
        }

        lines.Add("## Workspace");
        lines.Add($"Workspace: {workspaceDir}");
        lines.Add("Paths may be workspace-relative or absolute per tool behavior.");
        lines.Add("");

        var hostHints = HostPathHints.BuildBlock();
        if (!string.IsNullOrWhiteSpace(hostHints))
        {
            lines.Add("## Host paths (this machine)");
            lines.Add(hostHints);
            lines.Add("");
        }

        if (!RegistryHasTool(tools, "exec"))
        {
            lines.Add("## Runtime (no shell)");
            lines.Add(
                "No shell / subprocess tool is available in this profile. Do not rely on scripting runtimes to discover paths or list files — " +
                "use **Host paths** above and the path-oriented tools listed under **Tools**.");
            lines.Add("");
        }

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

    private static bool RegistryHasTool(IToolRegistry tools, string name) =>
        tools.All.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
