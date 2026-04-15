using System.Text.RegularExpressions;

namespace OpenLum.Console.Config;

/// <summary>
/// Loads skill metadata from the host bundle: <c>{hostRoot}/skills/*/SKILL.md</c> (or <c>Skills/</c>).
/// </summary>
public static class SkillLoader
{
    private static readonly Regex NameRe = new(@"^name:\s*(.+)$", RegexOptions.Multiline);
    private static readonly Regex DescRe =
        new(@"^description:\s*[""']?(.+?)[""']?\s*$", RegexOptions.Multiline);

    /// <summary>
    /// Returns the skills container under <paramref name="hostRoot"/> if it exists.
    /// Tries <c>skills</c> then <c>Skills</c> for case-sensitive file systems.
    /// </summary>
    private static string? GetSkillsContainerPath(string hostRoot)
    {
        var skillsPath = Path.Combine(hostRoot, "skills");
        var skillsPathCapital = Path.Combine(hostRoot, "Skills");
        if (Directory.Exists(skillsPath))
            return skillsPath;
        if (Directory.Exists(skillsPathCapital))
            return skillsPathCapital;
        return null;
    }

    /// <summary>
    /// Scan the host bundle for skills. Does not use the user workspace.
    /// </summary>
    public static IReadOnlyList<SkillEntry> Load(string hostRoot, int maxSkills = 50)
    {
        var results = new List<SkillEntry>();
        var container = GetSkillsContainerPath(hostRoot);
        if (container is null)
            return results;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in Directory.GetDirectories(container))
        {
            if (results.Count >= maxSkills)
                break;

            var skillPath = Path.Combine(sub, "SKILL.md");
            if (!File.Exists(skillPath))
                continue;

            var (desc, parsedName) = ParseFrontmatter(skillPath);
            var folderName = Path.GetFileName(sub);
            var skillName = !string.IsNullOrWhiteSpace(parsedName) ? parsedName : folderName;

            if (seen.Contains(skillName))
                continue;

            seen.Add(skillName);
            results.Add(new SkillEntry(skillName, desc, skillPath));
        }

        return results;
    }

    /// <summary>
    /// Builds the skills section for the system prompt.
    /// Aligns with OpenLum TS: inject metadata only; model loads SKILL.md via a path-capable tool when a skill is actually needed.
    /// Paths are compacted (home → ~) to save tokens.
    /// </summary>
    public static string FormatForPrompt(IReadOnlyList<SkillEntry> skills)
    {
        if (skills.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<available_skills>");
        foreach (var s in skills)
        {
            var location = CompactPath(s.Location);
            sb.AppendLine("  <skill>");
            sb.AppendLine($"    <name>{EscapeXml(s.Name)}</name>");
            sb.AppendLine($"    <description>{EscapeXml(s.Description)}</description>");
            sb.AppendLine($"    <location>{EscapeXml(location)}</location>");
            sb.AppendLine("  </skill>");
        }
        sb.AppendLine("</available_skills>");
        sb.AppendLine();
        sb.AppendLine(
            "**Mandatory read**: If the task **matches** any skill above (by name, description, or the domain of the work), you **must** `read` that skill's SKILL.md at the listed `<location>` **before** following skill-specific workflows, running skill-shipped executables, or relying on rules documented only in that file. Load it as soon as you recognize the match—any turn, including right after discovery—do not defer or skip."
        );
        sb.AppendLine(
            "**Later phases**: If relevance to a skill appears only after earlier steps (observe, build failure, user direction, scope change), you **must** `read` that SKILL.md **before** continuing skill-specific work in that phase. Do not rely on the short catalog here or on prior turns alone—pull the document when the skill becomes applicable."
        );
        sb.AppendLine(
            "**Skill as dynamic contract**: Phase-specific steps, commands, checks, and constraints belong in each SKILL.md; this section only lists skills. When a skill applies, treat its SKILL.md as the authoritative, step-by-step contract—not something to replace with generic system prompt habits."
        );
        sb.AppendLine(
            "**Tools without skills**: Work that **only** needs built-in **Tools** and does **not** depend on a listed skill's documented workflow does **not** require reading a skill—follow each tool's description."
        );
        sb.AppendLine(
            "Do **not** open SKILL.md or browse this catalog solely to access arbitrary user-supplied project files — use the path/file tools from **Tools**, following each tool's description."
        );
        sb.AppendLine(
            "Before running any executable shipped with a skill, read that skill's SKILL.md for the exact command and paths; do not infer binary names from skill titles."
        );
        return sb.ToString();
    }

    /// <summary>
    /// Compacts path: replaces user home prefix with ~ to save prompt tokens (matches TS compactSkillPaths).
    /// Uses forward slash in output for cross-platform readability.
    /// </summary>
    private static string CompactPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            return path;
        var normPath = path.Replace('\\', '/');
        var normHome = home.Replace('\\', '/').TrimEnd('/') + '/';
        if (normPath.StartsWith(normHome, StringComparison.OrdinalIgnoreCase))
            return "~/" + normPath[normHome.Length..];
        return path;
    }

    /// <summary>
    /// Returns the skills container path under <paramref name="hostRoot"/> if present (for ReadTool allowlists).
    /// </summary>
    public static IReadOnlyList<string> GetSkillRoots(string hostRoot)
    {
        var c = GetSkillsContainerPath(hostRoot);
        return c is null ? Array.Empty<string>() : [c];
    }

    private static (string Description, string Name) ParseFrontmatter(string path)
    {
        var text = File.ReadAllText(path);
        var match = Regex.Match(text, @"^---\s*\r?\n(.*?)\r?\n---", RegexOptions.Singleline);
        if (!match.Success)
            return (Path.GetFileName(Path.GetDirectoryName(path)) ?? "skill", "");

        var block = match.Groups[1].Value;
        var nameMatch = NameRe.Match(block);
        var descMatch = DescRe.Match(block);
        var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : "";
        var desc = descMatch.Success ? descMatch.Groups[1].Value.Trim() : $"Skill: {name}";
        return (desc, name);
    }

    private static string EscapeXml(string s)
    {
        return s.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
