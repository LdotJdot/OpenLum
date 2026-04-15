using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenLum.Console.Interfaces;
using OpenLum.Console.Models;
using OpenLum.Console.Tools;
using Xunit;

namespace OpenLum.Tests;

public sealed class SessionsSpawnToolParallelTests
{
    private sealed class DummyModel : IModelProvider
    {
        public Task<ModelResponse> ChatAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            IProgress<string>? contentProgress,
            CancellationToken ct = default)
        {
            // One-shot: always return an assistant message, no tools.
            // Echo the user prompt for identification.
            var user = "";
            for (var i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role == MessageRole.User)
                {
                    user = messages[i].Content ?? "";
                    break;
                }
            }
            return Task.FromResult(new ModelResponse("OK: " + user, Array.Empty<ToolCall>()));
        }
    }

    private sealed class EmptyTools : IToolRegistry
    {
        public IReadOnlyList<ITool> All => Array.Empty<ITool>();
        public ITool? Get(string name) => null;
    }

    [Fact]
    public async Task sessions_spawn_tasks_array_runs_parallel_and_returns_combined_output()
    {
        var tool = new SessionsSpawnTool(
            new DummyModel(),
            new EmptyTools(),
            workspaceDir: Environment.CurrentDirectory,
            systemPrompt: "sys",
            maxToolTurns: 3);

        // sessions_spawn parses tasks via JsonElement; pass raw JSON array.
        var json = System.Text.Json.JsonDocument.Parse("""
        [
          { "task": "t1", "label": "a" },
          { "task": "t2", "label": "b" }
        ]
        """).RootElement.Clone();

        var args = new Dictionary<string, object?>
        {
            ["tasks"] = json
        };

        var res = await tool.ExecuteAsync(args, CancellationToken.None);
        Assert.Contains("[子agent批量并行已结束", res, StringComparison.Ordinal);
        Assert.Contains("--- [a] ---", res, StringComparison.Ordinal);
        Assert.Contains("--- [b] ---", res, StringComparison.Ordinal);
        Assert.Contains("OK: t1", res, StringComparison.Ordinal);
        Assert.Contains("OK: t2", res, StringComparison.Ordinal);
    }
}

