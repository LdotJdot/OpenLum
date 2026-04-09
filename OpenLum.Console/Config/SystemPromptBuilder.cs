using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Config;

/// <summary>
/// Builds the system prompt from workspace, tools (filtered by policy), and skills.
/// Aligns with OpenLum's system-prompt structure.
/// </summary>
public static class SystemPromptBuilder
{
    /// <summary>Tool order for prompt. Search/read/edit tools first, then runtime/memory/sessions.</summary>
    private static readonly string[] ToolOrder =
    [
        "grep", "glob", "read", "write", "str_replace", "list_dir",
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
            "## Execution Process",
            "For trivial, one-step, clearly specified questions (e.g. simple Q&A, obvious calculations), you can answer directly and concisely.",
            "For any non-trivial task (fuzzy, incomplete, multi-step, risky, or with important trade-offs), follow this flow before acting:",
            "1. **Think intent** — Clarify what the user really wants and the task goals.",
            "2. **Understand the problem** — Identify context, constraints, and what information you need.",
            "3. **Solve** — Then call tools or answer, based on 1 and 2.",
            "",
            dateLine,
            "",
            "## Tools",
            toolLines.Count > 0 ? string.Join("\n", toolLines) : "(no tools)",
            "",
            "## Output",
            "By default, answer directly and concisely without visible thinking.",
            "Only when the task is complex, ambiguous, multi-step, or high-impact should you expose inner reasoning: wrap that reasoning (planning, weighing options, what you are about to do) inside <thinking>...</thinking> tags. The console will show that part in a separate yellow block. After thinking, output your actual reply or tool calls.",
            "",
            "## Tool Call Style",
            "Minimize tool calls. Avoid redundant or no-op operations.",
            "Narrate only when it helps; otherwise just call the tool.",
            "When you call sessions_spawn, the tool returns the sub-agent's final reply. Once you receive that result, treat it as the completed answer for that task—do not spawn another sub-agent for the same task or repeat the same search/operation yourself.",
            "",
            "## Search → Read → Edit workflow",
            "For code/text tasks, prefer this sequence:",
            "1. **glob** to find files by name pattern; **grep** to find content by regex. Use grep output_mode=\"files_with_matches\" for fast file discovery.",
            "2. **read** with offset/limit to inspect relevant sections (avoid reading entire large files).",
            "3. **str_replace** for small, precise edits (old_string must be unique); **write** only for new files or full rewrites.",
            "You can call multiple read-only tools (grep, glob, read, list_dir) in a single turn — they run in parallel for speed.",
            "For PDF/Word/Excel and other binary formats, use the corresponding skill via exec (see <available_skills>).",
            "",
            "## Safety",
            "Prioritize safety and human oversight. If instructions conflict, pause and ask.",
            ""
        };

        lines.Add("## Workspace");
        lines.Add($"Workspace: {workspaceDir}");
        lines.Add("All file tools accept workspace-relative paths (resolved against workspace) or absolute paths. When the user specifies an absolute path, use it directly. exec cwd is workspace root.");
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
