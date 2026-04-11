using OpenLum.Console.Agent;
using OpenLum.Console.Compaction;
using OpenLum.Console.Config;
using OpenLum.Console.Console;
using OpenLum.Console.Interfaces;
using OpenLum.Console.Observability;
using OpenLum.Console.Session;

namespace OpenLum.Console.Tools;

/// <summary>
/// Spawns a sub-agent to complete a task in an isolated session. Returns the result.
/// Sub-agent shares the same environment as the parent: same workspace, same tool instances
/// (read/write/list_dir/exec/memory_*), same model, same Skills section in prompt. Only differences:
/// (1) Fresh session (no prior chat); (2) No sessions_spawn tool and prompt lists one fewer tool;
/// (3) Extra "[Sub-agent: ...]" reminder in system prompt; (4) Optional compactor/limits aligned with config;
/// (5) Own TodoStore/PlanStore so workflow Observe→Act promotion matches todo/submit_plan calls (fixes exec gated behind Act).
/// </summary>
public sealed class SessionsSpawnTool : ITool
{
    private static readonly object ConsoleLock = new();

    private readonly IModelProvider _model;
    private readonly IToolRegistry _parentTools;
    private readonly string _workspaceDir;
    private readonly string _systemPrompt;
    private readonly int _maxDepth;
    private readonly SessionCompactor? _compactor;
    private readonly int _maxToolTurns;
    private readonly int? _maxToolResultChars;
    private readonly int? _maxFailedToolResultChars;
    private readonly bool _useModelDecideAtLimit;
    private readonly SessionRunLogger? _sessionRunLogger;
    private readonly WorkflowConfig _workflow;

    public SessionsSpawnTool(
        IModelProvider model,
        IToolRegistry parentTools,
        string workspaceDir,
        string systemPrompt,
        int maxDepth = 2,
        SessionCompactor? compactor = null,
        int maxToolTurns = 100,
        int? maxToolResultChars = null,
        int? maxFailedToolResultChars = null,
        bool useModelDecideAtLimit = true,
        SessionRunLogger? sessionRunLogger = null,
        WorkflowConfig? workflow = null)
    {
        _model = model;
        _parentTools = parentTools;
        _workspaceDir = workspaceDir;
        // Same base prompt as parent (Workspace, Skills, date, style); Tools section has no sessions_spawn.
        // Append sub-agent reminder so it uses skills (e.g. agent-browser for 联网搜索) the same way as parent.
        _systemPrompt = systemPrompt.TrimEnd() + "\n\n[Sub-agent: You have the same workspace, tools, and skills as the parent. You must use them the same way: when the task requires web search, browsing, or online information (联网搜索/浏览网页), use the read tool to load the relevant skill (e.g. agent-browser, search) from <available_skills> above, then use exec as described in that skill. Do not say you cannot do web search—you can, via skills and exec.]";
        _maxDepth = maxDepth;
        _compactor = compactor;
        _maxToolTurns = Math.Max(1, maxToolTurns);
        _maxToolResultChars = maxToolResultChars;
        _maxFailedToolResultChars = maxFailedToolResultChars;
        _useModelDecideAtLimit = useModelDecideAtLimit;
        _sessionRunLogger = sessionRunLogger;
        _workflow = workflow ?? new WorkflowConfig();
    }

    public string Name => "sessions_spawn";
    public string Description =>
        "Spawn a sub-agent to complete a task in an isolated session. Returns the sub-agent's final reply. " +
        "Once you receive the result, treat it as the answer for that delegated task—do not spawn again for the same work.";
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

        var label = args.GetValueOrDefault("label")?.ToString();
        var subagentLabel = string.IsNullOrWhiteSpace(label) ? "sub-agent" : $"sub-agent:{label}";

        // Isolated plan/todo stores + same workflow as parent so Observe→Act promotion matches submit_plan/todo tool effects.
        var subTodoStore = new TodoStore();
        var subPlanStore = new PlanStore();
        var subTools = CreateSubagentToolRegistry(subTodoStore, subPlanStore);
        var subSession = new ConsoleSession();
        var subLog = _sessionRunLogger?.CreateScoped(subagentLabel);
        var subAgent = new AgentLoop(
            _model,
            subTools,
            subSession,
            _systemPrompt,
            compactor: _compactor,
            todoStore: subTodoStore,
            planStore: subPlanStore,
            workflow: _workflow,
            runLogger: subLog,
            onToolStarting: (name, arguments) =>
            {
                lock (ConsoleLock)
                {
                    var prev = System.Console.ForegroundColor;
                    System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                    System.Console.WriteLine();
                    System.Console.WriteLine($"    [{subagentLabel}] {ToolActivityFormatter.FormatStarting(name, arguments)}");
                    System.Console.ForegroundColor = prev;
                }
            },
            onToolCompleted: (name, arguments, success) =>
            {
                lock (ConsoleLock)
                {
                    var prev = System.Console.ForegroundColor;
                    System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                    System.Console.WriteLine($"    [{subagentLabel}] {ToolActivityFormatter.FormatCompleted(name, arguments, success)}");
                    System.Console.ForegroundColor = prev;
                }
            },
            maxToolResultChars: _maxToolResultChars,
            maxFailedToolResultChars: _maxFailedToolResultChars,
            maxToolTurns: _maxToolTurns,
            useModelDecideAtLimit: _useModelDecideAtLimit);

        subLog?.WriteLine($"sessions_spawn task: {task}");

        var startedStreaming = false;
        var thinkProgress = new ThinkAwareConsoleProgress(ConsoleLock);
        var progress = new Progress<string>(chunk =>
        {
            lock (ConsoleLock)
            {
                if (!startedStreaming)
                {
                    startedStreaming = true;
                    System.Console.WriteLine();
                    var prev = System.Console.ForegroundColor;
                    System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                    System.Console.Write($"[{subagentLabel}] ");
                    System.Console.ForegroundColor = prev;
                }
                thinkProgress.Report(chunk);
            }
        });

        var result = await subAgent.RunAsync(task, progress, ct);
        lock (ConsoleLock)
        {
            thinkProgress.FlushRemaining();
        }
        if (startedStreaming)
        {
            lock (ConsoleLock)
            {
                System.Console.WriteLine();
            }
        }

        if (!result.Success)
        {
            lock (ConsoleLock)
            {
                var prev = System.Console.ForegroundColor;
                System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                System.Console.WriteLine($"[{subagentLabel}] 已结束（失败）: {result.ErrorMessage ?? "unknown"}");
                System.Console.ForegroundColor = prev;
            }
            return $"Sub-agent failed: {result.ErrorMessage ?? "unknown"}";
        }

        lock (ConsoleLock)
        {
            var prev = System.Console.ForegroundColor;
            System.Console.ForegroundColor = System.ConsoleColor.Cyan;
            System.Console.WriteLine($"[{subagentLabel}] 已结束，结果已返回主 agent。");
            System.Console.ForegroundColor = prev;
        }

        var lastAssistant = subSession.Messages
            .Where(m => m.Role == Models.MessageRole.Assistant)
            .LastOrDefault();
        var reply = lastAssistant?.Content ?? "(no reply)";
        // 在返回内容前加一行明确标识，便于主 agent 识别“子 agent 已完成并携带以下结果”
        return $"[子agent已结束，以下为子agent的最终回复]\n\n{reply}";
    }

    private IToolRegistry CreateSubagentToolRegistry(TodoStore todoStore, PlanStore planStore)
    {
        var filtered = new ToolRegistry();
        foreach (var t in _parentTools.All)
        {
            if (t.Name == "sessions_spawn")
                continue;
            if (string.Equals(t.Name, "todo", StringComparison.OrdinalIgnoreCase))
            {
                filtered.Register(new TodoTool(todoStore));
                continue;
            }

            if (string.Equals(t.Name, "submit_plan", StringComparison.OrdinalIgnoreCase))
            {
                filtered.Register(new SubmitPlanTool(planStore));
                continue;
            }

            filtered.Register(t);
        }

        return filtered;
    }
}
