using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenLum.Console.Config;
using OpenLum.Console.Tools;
using Xunit;

namespace OpenLum.Tests;

public class ToolPolicyFilterTests
{
    private sealed class DummyTool : OpenLum.Console.Interfaces.ITool
    {
        public string Name { get; init; } = "";
        public string Description => "";
        public IReadOnlyList<OpenLum.Console.Interfaces.ToolParameter> Parameters => Array.Empty<OpenLum.Console.Interfaces.ToolParameter>();
        public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default) => Task.FromResult(string.Empty);
    }

    private sealed class DummyRegistry : OpenLum.Console.Interfaces.IToolRegistry
    {
        private readonly IReadOnlyList<OpenLum.Console.Interfaces.ITool> _tools;
        public DummyRegistry(params string[] names)
        {
            _tools = names.Select(n => (OpenLum.Console.Interfaces.ITool)new DummyTool { Name = n }).ToList();
        }

        public IReadOnlyList<OpenLum.Console.Interfaces.ITool> All => _tools;
        public OpenLum.Console.Interfaces.ITool? Get(string name) =>
            _tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DenyAll_DisablesAllTools()
    {
        var registry = new DummyRegistry("read", "write", "exec");
        var policy = new ToolPolicyConfig
        {
            Profile = "local",
            Allow = [],
            Deny = ["*"]
        };

        var filtered = new ToolPolicyFilter(registry, policy);

        Assert.Empty(filtered.All);
        Assert.Null(filtered.Get("read"));
        Assert.Null(filtered.Get("exec"));
    }
}

