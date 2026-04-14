using System.Text.RegularExpressions;

namespace OpenLum.Console.Config;

/// <summary>
/// Loads skill metadata from workspace/skills/*/SKILL.md (or Skills with capital S for case-sensitive FS).
/// </summary>
public static class SkillLoader
{
    private static readonly Regex NameRe = new(@"^name:\s*(.+)$", RegexOptions.Multiline);
    private static readonly Regex DescRe =
        new(@"^description:\s*[""']?(.+?)[""']?\s*$", RegexOptions.Multiline);

    /// <summary>
    /// Enumerates skill root directories in precedence order. Tries both "skills" and "Skills"
    /// so that repo folder "Skills" is found on case-sensitive file systems (e.g. Linux).
    /// </summary>
    private static IEnumerable<string> EnumerateSkillRoots(string workspaceDir)
    {
        var baseDirs = new[]
        {
            workspaceDir,
            AppContext.BaseDirectory,
            Path.GetDirectoryName(AppContext.BaseDirectory) ?? "."
        };
        foreach (var baseDir in baseDirs)
        {
            var skillsPath = Path.Combine(baseDir, "skills");
            var skillsPathCapital = Path.Combine(baseDir, "Skills");
            if (Directory.Exists(skillsPath))
                yield return skillsPath;
            else if (Directory.Exists(skillsPathCapital))
                yield return skillsPathCapital;
        }
    }

    /// <summary>
    /// Scan workspace for skills. Looks for skills/*/SKILL.md and workspace/skills/*/SKILL.md.
    /// </summary>
    public static IReadOnlyList<SkillEntry> Load(string workspaceDir, int maxSkills = 50)
    {
        var results = new List<SkillEntry>();
        var dirs = EnumerateSkillRoots(workspaceDir).ToList();

        // Use effective skill name (frontmatter name if present, otherwise folder name)
        // as the unique key, and define a clear precedence:
        //   workspace/skills  >  AppContext.BaseDirectory/skills  >  parent-of-AppContext/skills
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir) || results.Count >= maxSkills)
                continue;

            foreach (var sub in Directory.GetDirectories(dir))
            {
                var skillPath = Path.Combine(sub, "SKILL.md");
                if (!File.Exists(skillPath))
                    continue;

                var (desc, parsedName) = ParseFrontmatter(skillPath);
                var folderName = Path.GetFileName(sub);
                var skillName = !string.IsNullOrWhiteSpace(parsedName) ? parsedName : folderName;

                // If a skill with the same effective name has already been loaded from a
                // higher‑priority root, skip this one.
                if (seen.Contains(skillName))
                    continue;

                seen.Add(skillName);
                results.Add(new SkillEntry(skillName, desc, skillPath));

                if (results.Count >= maxSkills)
                    break;
            }
        }

        return results;
    }

    /// <summary>
    /// Builds the skills section for the system prompt.
    /// Aligns with OpenLum TS: inject metadata only; model loads SKILL.md via a path-capable tool when a skill is actually needed.
    /// Paths are compacted (home → ~) to save tokens.
    /// </summary>
    public static string FormatForPrompt(IReadOnlyList<SkillEntry> skills, string workspaceDir)
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
            "Load a skill's SKILL.md from the listed path **only when the user's task actually requires that skill**."
        );
        sb.AppendLine(
            "Do **not** open SKILL.md or browse this catalog solely to access arbitrary user-supplied files — use the path/file tools from the **Tools** section, following each tool's description."
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
    /// Returns the directory roots used for skill discovery (for ReadTool to allow reading SKILL.md).
    /// Uses same enumeration as Load so both "skills" and "Skills" are found on case-sensitive FS.
    /// </summary>
    public static IReadOnlyList<string> GetSkillRoots(string workspaceDir)
    {
        return EnumerateSkillRoots(workspaceDir)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
