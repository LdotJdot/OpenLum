using System.IO;
using OpenLum.Console.Config;
using Xunit;

namespace OpenLum.Tests;

public class SkillLoaderTests
{
    [Fact]
    public void Load_UsesFrontmatterNameAndRespectsPrecedence()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var workspaceSkills = Path.Combine(temp.FullName, "skills");
            Directory.CreateDirectory(workspaceSkills);
            var s1 = Path.Combine(workspaceSkills, "foo");
            Directory.CreateDirectory(s1);
            File.WriteAllText(Path.Combine(s1, "SKILL.md"), """
            ---
            name: foo-skill
            description: "from workspace"
            ---
            """);

            var entries = SkillLoader.Load(temp.FullName, maxSkills: 10);
            Assert.True(entries.Count >= 1, "At least workspace skill should be loaded");
            // Workspace has highest precedence: first entry must be from our temp workspace
            Assert.Equal("foo-skill", entries[0].Name);
            Assert.Contains("from workspace", entries[0].Description);
        }
        finally
        {
            temp.Delete(true);
        }
    }
}

