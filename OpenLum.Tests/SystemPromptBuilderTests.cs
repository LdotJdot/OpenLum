using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenLum.Console.Config;
using Xunit;

namespace OpenLum.Tests;

public class SystemPromptBuilderTests
{
    private sealed class DummyTool : OpenLum.Console.Interfaces.ITool
    {
        public string Name { get; init; } = "";
        public string Description => "d";
        public IReadOnlyList<OpenLum.Console.Interfaces.ToolParameter> Parameters =>
            Array.Empty<OpenLum.Console.Interfaces.ToolParameter>();
        public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default) =>
            Task.FromResult("");
    }

    private sealed class DummyRegistry : OpenLum.Console.Interfaces.IToolRegistry
    {
        private readonly IReadOnlyList<OpenLum.Console.Interfaces.ITool> _tools;

        public DummyRegistry(params string[] names) =>
            _tools = names.Select(n => (OpenLum.Console.Interfaces.ITool)new DummyTool { Name = n }).ToList();

        public IReadOnlyList<OpenLum.Console.Interfaces.ITool> All => _tools;
        public OpenLum.Console.Interfaces.ITool? Get(string name) =>
            _tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_IncludesEfficiencySection_WithSpawnBullet()
    {
        var r = new DummyRegistry("grep", "read", "list_dir", "sessions_spawn");
        var p = SystemPromptBuilder.Build(Path.GetTempPath(), Path.GetTempPath(), r);
        Assert.Contains("## Efficiency", p, StringComparison.Ordinal);
        Assert.Contains("Principles only", p, StringComparison.Ordinal);
        Assert.Contains("one assistant turn", p, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Delegation / sub-session", p, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OmitsSpawnEfficiencyBulletWhenToolMissing()
    {
        var r = new DummyRegistry("grep", "read");
        var p = SystemPromptBuilder.Build(Path.GetTempPath(), Path.GetTempPath(), r);
        Assert.Contains("## Efficiency", p, StringComparison.Ordinal);
        Assert.DoesNotContain("Delegation / sub-session", p, StringComparison.Ordinal);
    }
}
