namespace OpenLum.Console.Config;

/// <summary>
/// Metadata for a skill (name, description, path to SKILL.md).
/// </summary>
public sealed record SkillEntry(string Name, string Description, string Location);
