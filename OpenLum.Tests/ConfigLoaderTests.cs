using System;
using System.IO;
using OpenLum.Console.Config;
using Xunit;

namespace OpenLum.Tests;

public class ConfigLoaderTests
{
    [Fact]
    public void Load_ReturnsDefaults_WhenNoFiles()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var cfg = ConfigLoader.Load(temp.FullName);
            Assert.NotNull(cfg);
            Assert.Equal("coding", cfg.ToolPolicy.Profile);
            Assert.Equal("openai", cfg.Model.Provider);
        }
        finally
        {
            temp.Delete(true);
        }
    }

    [Fact]
    public void Load_UsesStrictConfigEnv_ToFailOnInvalidJson()
    {
        var temp = Directory.CreateTempSubdirectory();
        var path = Path.Combine(temp.FullName, "openlum.json");
        File.WriteAllText(path, "{ invalid json");

        var original = Environment.GetEnvironmentVariable("OPENLUM_STRICT_CONFIG");
        Environment.SetEnvironmentVariable("OPENLUM_STRICT_CONFIG", "1");
        try
        {
            Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(temp.FullName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENLUM_STRICT_CONFIG", original);
            temp.Delete(true);
        }
    }
}

