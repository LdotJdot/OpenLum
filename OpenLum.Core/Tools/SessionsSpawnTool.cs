using OpenLum.Console.Agent;
using OpenLum.Console.Compaction;
using OpenLum.Console.Config;
using OpenLum.Console.Console;
using OpenLum.Console.Interfaces;
using OpenLum.Console.Observability;
using OpenLum.Console.Session;
using System.Text;
using System.Text.Json;

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
    private readonly Action<string>? _onPlanCommitted;

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
        WorkflowConfig? workflow = null,
        Action<string>? onPlanCommitted = null)
    {
        _model = model;
        _parentTools = parentTools;
        _workspaceDir = workspaceDir;
        // Same base prompt as parent; Tools section has no sessions_spawn.
        _systemPrompt = systemPrompt.TrimEnd() + "\n\n[Sub-agent: You share the parent's workspace and the same system prompt (including **Skills** and **Tools**). Apply it the same way as the parent session.]";
        _maxDepth = maxDepth;
        _compactor = compactor;
        _maxToolTurns = Math.Max(1, maxToolTurns);
        _maxToolResultChars = maxToolResultChars;
        _maxFailedToolResultChars = maxFailedToolResultChars;
        _useModelDecideAtLimit = useModelDecideAtLimit;
        _sessionRunLogger = sessionRunLogger;
        _workflow = workflow ?? new WorkflowConfig();
        _onPlanCommitted = onPlanCommitted;
    }

    public string Name => "sessions_spawn";
    public string Description =>
        "Spawn one or more sub-agents in isolated sessions (same workspace/tools, fresh chat). " +
        "Use this for parallel investigation or long multi-step work you want to isolate. " +
        "Prefer doing simple read/search/list work directly in the parent. " +
        "Input: either 'task' (single) or 'tasks' array (parallel). Output: the sub-agent(s) final reply, combined. " +
        "Safety: avoid concurrent edits to the same file; default sub-agents to evidence + proposals, then apply edits deterministically in the parent. " +
        "Delegation rule: keep tasks short and outcome-based: Goal + Scope + Output + WriteAllowed (default false).";
    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("task", "string", "The task for the sub-agent to complete", true),
        new ToolParameter("label", "string", "Optional label for the sub-agent run", false),
        new ToolParameter("tasks", "array", "Optional array of { task, label? }. If provided, runs them in parallel and returns combined results.", false)
    ];

    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        if (TryParseTasks(args, out var tasks, out var parseErr))
        {
            if (tasks.Count == 0)
                return "Error: tasks is empty.";
            return await RunManyAsync(tasks, ct);
        }
        if (!string.IsNullOrWhiteSpace(parseErr))
            return parseErr!;

        var task = args.GetValueOrDefault("task")?.ToString();
        if (string.IsNullOrWhiteSpace(task))
            return "Error: task is required (or pass tasks array).";

        var label = args.GetValueOrDefault("label")?.ToString();
        var reply = await RunSingleAsync(new SpawnTask(task, label), ct);
        // 在返回内容前加一行明确标识，便于主 agent 识别“子 agent 已完成并携带以下结果”
        return $"[子agent已结束，以下为子agent的最终回复]\n\n{reply}";
    }

    private async Task<string> RunManyAsync(IReadOnlyList<SpawnTask> tasks, CancellationToken ct)
    {
        var runtime = tasks.Select((t, idx) => new SpawnRuntime(idx, t)).ToArray();

        lock (ConsoleLock)
        {
            System.Console.WriteLine();
            System.Console.WriteLine($"[sessions_spawn] 并行启动 {runtime.Length} 个子agent…（将以新增行方式持续刷新状态）");
            WriteStatusSnapshot(runtime, "已创建任务");
        }

        var runs = new Task<(SpawnTask Task, string Reply, bool Ok)>[tasks.Count];
        for (var i = 0; i < tasks.Count; i++)
        {
            var rt = runtime[i];
            runs[i] = Task.Run(async () =>
            {
                MarkStatus(runtime, rt.Index, SpawnState.Running, "执行中…");
                try
                {
                    var reply = await RunSingleAsync(rt.Task, ct, bufferedOutput: true);
                    MarkStatus(runtime, rt.Index, SpawnState.Completed, "已完成");
                    return (rt.Task, reply, true);
                }
                catch (OperationCanceledException)
                {
                    MarkStatus(runtime, rt.Index, SpawnState.Cancelled, "已取消");
                    return (rt.Task, "Sub-agent cancelled.", false);
                }
                catch (Exception ex)
                {
                    MarkStatus(runtime, rt.Index, SpawnState.Failed, "失败");
                    return (rt.Task, $"Sub-agent failed: {ex.Message}", false);
                }
            }, ct);
        }

        var done = await Task.WhenAll(runs);
        var sb = new StringBuilder();
        sb.AppendLine("[子agent批量并行已结束，以下为各子agent最终回复]");
        sb.AppendLine();
        for (var i = 0; i < done.Length; i++)
        {
            var (st, reply, ok) = done[i];
            var label = string.IsNullOrWhiteSpace(st.Label) ? $"#{i + 1}" : st.Label!;
            sb.AppendLine($"--- [{label}] --- {(ok ? "(OK)" : "(ERROR)")}");
            sb.AppendLine(reply);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private async Task<string> RunSingleAsync(SpawnTask req, CancellationToken ct)
        => await RunSingleAsync(req, ct, bufferedOutput: false);

    private async Task<string> RunSingleAsync(SpawnTask req, CancellationToken ct, bool bufferedOutput)
    {
        var task = req.Task;
        var label = req.Label;
        var subagentLabel = string.IsNullOrWhiteSpace(label) ? "sub-agent" : $"sub-agent:{label}";

        // Isolated plan/todo stores + same workflow as parent so Observe→Act promotion matches submit_plan/todo tool effects.
        var subTodoStore = new TodoStore();
        var subPlanStore = new PlanStore();
        var subTools = CreateSubagentToolRegistry(subTodoStore, subPlanStore);
        var subSession = new ConsoleSession();
        var subLog = _sessionRunLogger?.CreateScoped(subagentLabel);
        var thinkProgress = new ThinkAwareConsoleProgress(ConsoleLock);
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
            useModelDecideAtLimit: _useModelDecideAtLimit,
            flushAssistantStreamBeforeTools: () =>
            {
                lock (ConsoleLock)
                {
                    thinkProgress.FlushRemaining();
                    System.Console.WriteLine();
                }
            });

        subLog?.WriteLine($"sessions_spawn task: {task}");

        var startedStreaming = false;
        var buffered = bufferedOutput ? new BufferedConsoleProgress(ConsoleLock) : null;
        var progress = new Progress<string>(chunk =>
        {
            lock (ConsoleLock)
            {
                if (!startedStreaming && !string.IsNullOrEmpty(chunk))
                {
                    startedStreaming = true;
                    System.Console.WriteLine();
                    var prev = System.Console.ForegroundColor;
                    System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                    System.Console.Write($"[{subagentLabel}] ");
                    System.Console.ForegroundColor = prev;
                }

                if (buffered is null)
                {
                    thinkProgress.Report(chunk);
                    return;
                }

                // In parallel mode, buffer to reduce A/B interleaving.
                // Flush on newline or minimum chunk size, but still keep the stream feel.
                foreach (var part in buffered.SplitAndBuffer(chunk))
                    thinkProgress.Report(part);
            }
        });

        var result = await subAgent.RunAsync(task, progress, ct);
        lock (ConsoleLock)
        {
            if (buffered is not null)
            {
                var tail = buffered.FlushAll();
                if (!string.IsNullOrEmpty(tail))
                    thinkProgress.Report(tail);
            }
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
        return lastAssistant?.Content ?? "(no reply)";
    }

    private sealed record SpawnTask(string Task, string? Label);

    /// <summary>
    /// Buffers streamed chunks and yields flushable parts to reduce interleaving in parallel runs.
    /// Strategy: flush whenever we see a newline, or when the buffer exceeds a threshold.
    /// </summary>
    private sealed class BufferedConsoleProgress
    {
        private readonly object _lock;
        private readonly StringBuilder _buf = new();
        private const int FlushThresholdChars = 600;

        public BufferedConsoleProgress(object consoleLock) => _lock = consoleLock;

        public IEnumerable<string> SplitAndBuffer(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
                yield break;

            // Caller already holds ConsoleLock; keep internal invariants anyway.
            lock (_lock)
            {
                _buf.Append(chunk);

                while (true)
                {
                    var idx = IndexOfNewline(_buf);
                    if (idx >= 0)
                    {
                        var take = idx + 1;
                        yield return _buf.ToString(0, take);
                        _buf.Remove(0, take);
                        continue;
                    }

                    if (_buf.Length >= FlushThresholdChars)
                    {
                        yield return _buf.ToString();
                        _buf.Clear();
                    }
                    break;
                }
            }
        }

        public string FlushAll()
        {
            lock (_lock)
            {
                if (_buf.Length == 0) return "";
                var s = _buf.ToString();
                _buf.Clear();
                return s;
            }
        }

        private static int IndexOfNewline(StringBuilder sb)
        {
            for (var i = 0; i < sb.Length; i++)
            {
                if (sb[i] == '\n')
                    return i;
            }
            return -1;
        }
    }

    private enum SpawnState
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    private sealed class SpawnRuntime
    {
        public int Index { get; }
        public SpawnTask Task { get; }
        public SpawnState State { get; set; } = SpawnState.Pending;
        public string Note { get; set; } = "等待中";
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }

        public SpawnRuntime(int index, SpawnTask task)
        {
            Index = index;
            Task = task;
        }
    }

    private static void MarkStatus(SpawnRuntime[] runtime, int index, SpawnState state, string note)
    {
        lock (ConsoleLock)
        {
            var rt = runtime[index];
            if (state == SpawnState.Running && rt.State == SpawnState.Pending)
                rt.StartedAt = DateTimeOffset.Now;
            rt.State = state;
            rt.Note = note;
            if (state is SpawnState.Completed or SpawnState.Failed or SpawnState.Cancelled)
                rt.EndedAt = DateTimeOffset.Now;
            WriteStatusSnapshot(runtime, $"状态更新: #{index + 1} -> {StateText(state)}");
        }
    }

    private static void WriteStatusSnapshot(SpawnRuntime[] runtime, string title)
    {
        System.Console.WriteLine($"[sessions_spawn] {title}");
        for (var i = 0; i < runtime.Length; i++)
        {
            var rt = runtime[i];
            var label = string.IsNullOrWhiteSpace(rt.Task.Label) ? $"#{i + 1}" : rt.Task.Label!;
            var dur = rt.State == SpawnState.Running && rt.StartedAt != default
                ? $" ({(DateTimeOffset.Now - rt.StartedAt).TotalSeconds:F0}s)"
                : rt.EndedAt is { } end && rt.StartedAt != default
                    ? $" ({(end - rt.StartedAt).TotalSeconds:F0}s)"
                    : "";
            System.Console.WriteLine($"  - {i + 1}. [{label}] {StateText(rt.State)}{dur} — {rt.Note}");
        }
        System.Console.WriteLine();
    }

    private static string StateText(SpawnState s) => s switch
    {
        SpawnState.Pending => "等待中",
        SpawnState.Running => "执行中",
        SpawnState.Completed => "已完成",
        SpawnState.Failed => "失败",
        SpawnState.Cancelled => "已取消",
        _ => s.ToString()
    };

    private static bool TryParseTasks(
        IReadOnlyDictionary<string, object?> args,
        out List<SpawnTask> tasks,
        out string? error)
    {
        tasks = new List<SpawnTask>();
        error = null;

        if (!args.TryGetValue("tasks", out var val) || val is null)
            return false;

        if (val is not JsonElement je)
        {
            error = "Error: tasks must be a JSON array.";
            return false;
        }

        if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined)
            return false;

        if (je.ValueKind != JsonValueKind.Array)
        {
            error = "Error: tasks must be an array.";
            return false;
        }

        foreach (var item in je.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                error = "Error: tasks array items must be objects { task, label? }.";
                return false;
            }

            var task = item.TryGetProperty("task", out var tp) && tp.ValueKind == JsonValueKind.String ? tp.GetString() : null;
            var label = item.TryGetProperty("label", out var lp) && lp.ValueKind == JsonValueKind.String ? lp.GetString() : null;
            if (string.IsNullOrWhiteSpace(task))
            {
                error = "Error: each tasks[] item must include non-empty 'task' string.";
                return false;
            }

            tasks.Add(new SpawnTask(task!, label));
        }

        return true;
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
                filtered.Register(new SubmitPlanTool(planStore, _onPlanCommitted));
                continue;
            }

            // Ensure sub-agents get an isolated exec state (cwd cache, timeouts, etc.).
            // Parent and sub-agent should not share mutable exec state.
            if (string.Equals(t.Name, "exec", StringComparison.OrdinalIgnoreCase))
            {
                filtered.Register(new ExecTool(_workspaceDir));
                continue;
            }

            filtered.Register(t);
        }

        return filtered;
    }
}
