using OpenLum.Console.Agent;
using OpenLum.Console.Interfaces;
using OpenLum.Console.Session;

namespace OpenLum.Console.Tools;

/// <summary>
/// Spawns a sub-agent to complete a task in an isolated session. Returns the result.
/// </summary>
public sealed class SessionsSpawnTool : ITool
{
    private readonly IModelProvider _model;
    private readonly IToolRegistry _parentTools;
    private readonly string _workspaceDir;
    private readonly string _systemPrompt;
    private readonly int _maxDepth;

    public SessionsSpawnTool(
        IModelProvider model,
        IToolRegistry parentTools,
        string workspaceDir,
        string systemPrompt,
        int maxDepth = 2)
    {
        _model = model;
        _parentTools = parentTools;
        _workspaceDir = workspaceDir;
        _systemPrompt = systemPrompt;
        _maxDepth = maxDepth;
    }

    public string Name => "sessions_spawn";
    public string Description => "Spawn a sub-agent to complete a task in an isolated session. Returns the sub-agent's final reply.";
    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("task", "string", "The task for the sub-agent to complete", true),
        new ToolParameter("label", "string", "Optional label for the sub-agent run", false)
    ];

    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var task = args.GetValueOrDefault("task")?.ToString();
        if (string.IsNullOrWhiteSpace(task))
            return "Error: task is required.";

        var subTools = CreateSubagentToolRegistry();
        var subSession = new ConsoleSession();
        var subAgent = new AgentLoop(_model, subTools, subSession, _systemPrompt, compactor: null);

        var result = await subAgent.RunAsync(task, null, ct);
        if (!result.Success)
            return $"Sub-agent failed: {result.ErrorMessage ?? "unknown"}";

        var lastAssistant = subSession.Messages
            .Where(m => m.Role == Models.MessageRole.Assistant)
            .LastOrDefault();
        return lastAssistant?.Content ?? "(no reply)";
    }

    private IToolRegistry CreateSubagentToolRegistry()
    {
        var filtered = new ToolRegistry();
        foreach (var t in _parentTools.All)
        {
            if (t.Name == "sessions_spawn")
                continue;
            filtered.Register(t);
        }
        return filtered;
    }
}
