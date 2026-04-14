using System.Text.Json;
using OpenLum.Console.Config;
using OpenLum.Console.Compaction;
using OpenLum.Console.Interfaces;
using OpenLum.Console.Models;
using OpenLum.Console.Observability;

namespace OpenLum.Console.Agent;

/// <summary>
/// Runs the agent loop: user prompt → model → execute tools → model → ... → done.
/// </summary>
public sealed class AgentLoop : IAgent
{
    // Intent/thinking strategy lives in SystemPromptBuilder; optional <thinking> for complex tasks.
    private static string WrapUserInstruction(string userPrompt)
    {
        return (userPrompt ?? "").Trim();
    }
    private readonly IModelProvider _model;
    private readonly IToolRegistry _toolsBase;
    private readonly ISession _session;
    private readonly string _systemPrompt;
    private readonly SessionCompactor? _compactor;
    private readonly Tools.TodoStore? _todoStore;
    private readonly Tools.PlanStore? _planStore;
    private readonly WorkflowConfig _workflow;
    private readonly Action<string, string>? _onToolStarting;
    private readonly Action<string, string, bool>? _onToolCompleted;
    private readonly int? _maxToolResultChars;
    private readonly int? _maxFailedToolResultChars;
    private readonly int _maxToolTurns;
    private readonly bool _useModelDecideAtLimit;
    private readonly SessionRunLogger? _runLogger;
    /// <summary>Flush streamed assistant text to console before tool status lines (e.g. ThinkAwareConsoleProgress.FlushRemaining).</summary>
    private readonly Action? _flushAssistantStreamBeforeTools;

    private enum WorkflowPhase
    {
        Observe,
        ActPrep,
        Act,
        Verify
    }

    /// <summary>Observe phase base tools + browser + sub-agent; exec optional via <see cref="WorkflowConfig.AllowExecInObserve"/>.</summary>
    private HashSet<string> BuildObservePhaseAllowlist()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "grep", "glob", "read", "read_many", "list_dir",
            "memory_get", "memory_search", "todo", "submit_plan"
        };
        foreach (var name in ToolProfiles.Expand(["group:web", "group:sessions"]))
            set.Add(name);
        if (_workflow.AllowExecInObserve)
            set.Add("exec");
        return set;
    }

    private WorkflowPhase _phase = WorkflowPhase.Act;

    public AgentLoop(
        IModelProvider model,
        IToolRegistry tools,
        ISession session,
        string systemPrompt,
        SessionCompactor? compactor = null,
        Tools.TodoStore? todoStore = null,
        Tools.PlanStore? planStore = null,
        WorkflowConfig? workflow = null,
        Action<string, string>? onToolStarting = null,
        Action<string, string, bool>? onToolCompleted = null,
        int? maxToolResultChars = null,
        int? maxFailedToolResultChars = null,
        int maxToolTurns = 100,
        bool useModelDecideAtLimit = true,
        SessionRunLogger? runLogger = null,
        Action? flushAssistantStreamBeforeTools = null)
    {
        _model = model;
        _toolsBase = tools;
        _session = session;
        _systemPrompt = systemPrompt;
        _compactor = compactor;
        _todoStore = todoStore;
        _planStore = planStore;
        _workflow = workflow ?? new WorkflowConfig();
        _onToolStarting = onToolStarting;
        _onToolCompleted = onToolCompleted;
        _maxToolResultChars = maxToolResultChars;
        _maxFailedToolResultChars = maxFailedToolResultChars;
        _maxToolTurns = Math.Max(1, maxToolTurns);
        _useModelDecideAtLimit = useModelDecideAtLimit;
        _runLogger = runLogger;
        _flushAssistantStreamBeforeTools = flushAssistantStreamBeforeTools;
    }

    private static int ApproxChatChars(IReadOnlyList<ChatMessage> msgs)
    {
        var n = 0;
        foreach (var m in msgs)
        {
            n += m.Content?.Length ?? 0;
            if (m.ToolCalls is { Count: > 0 } tc)
            {
                foreach (var t in tc)
                    n += t.Name.Length + (t.Arguments?.Length ?? 0);
            }
        }

        return n;
    }

    public async Task<AgentTurnResult> RunAsync(
        string userPrompt,
        IProgress<string>? contentProgress,
        CancellationToken ct = default)
    {
        ResetWorkflowForNewUserMessage();
        _session.Add(new ChatMessage { Role = MessageRole.User, Content = WrapUserInstruction(userPrompt) });
        _runLogger?.WriteUserMessage(WrapUserInstruction(userPrompt));

        var maxTurns = _maxToolTurns;
        const int warningAt = 5; // Start warning this many turns before limit

        for (var turn = 0; turn < maxTurns; turn++)
        {
            if (_compactor is { } c && _session is ICompactableSession cs)
            {
                await c.CompactIfNeededAsync(cs, ct);
            }

            MaybePromoteObserveToAct();

            var remaining = maxTurns - turn;
            var messages = BuildMessages(remaining <= warningAt ? remaining : null);
            _runLogger?.WriteModelRound(turn, _phase.ToString(), messages.Count, ApproxChatChars(messages));
            var toolsForTurn = GetToolsForCurrentPhase();
            var toolDefs = toolsForTurn.All.Select(t => new ToolDefinition(
                t.Name,
                t.Description,
                t.Parameters
            )).ToList();

            ModelResponse response;
            try
            {
                response = await _model.ChatAsync(messages, toolDefs, contentProgress, ct);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"[Agent] Model error: {ex.GetType().Name}: {ex.Message}");
                _runLogger?.WriteError("model.ChatAsync", ex);
                return new AgentTurnResult(false, ex.Message);
            }

            _runLogger?.WriteAssistantResponse(response.Content, response.ToolCalls, response.Usage);

            if (response.ToolCalls.Count == 0)
            {
                _session.AddAssistant(response.Content, null);
                if (_workflow.Enabled && _phase == WorkflowPhase.ActPrep)
                {
                    _phase = WorkflowPhase.Act;
                    continue;
                }

                return new AgentTurnResult(true, null);
            }

            if (turn == maxTurns - 1)
            {
                // Hit limit but model still wants to call tools.
                if (_useModelDecideAtLimit)
                    return await DecideAtLimitAsync(response, contentProgress, ct);
                return await ForceWrapUpAsync(contentProgress, ct);
            }

            _session.AddAssistant(response.Content, response.ToolCalls);
            _flushAssistantStreamBeforeTools?.Invoke();
            var results = await ExecuteToolCallsAsync(response.ToolCalls, ct);
            _session.AddToolResults(results);
        }

        _runLogger?.WriteLine("agent_run_finished: max_tool_turns_exceeded");
        return new AgentTurnResult(false, "Max tool turns exceeded.");
    }

    /// <summary>When limit hit, ask model to choose: stop, continue (one more round), or ask user.</summary>
    private async Task<AgentTurnResult> DecideAtLimitAsync(
        ModelResponse lastResponse,
        IProgress<string>? contentProgress,
        CancellationToken ct)
    {
        _runLogger?.WriteLine("turn_limit_reached: invoking limit_decision flow (pending tool_calls discarded for session protocol)");
        // IMPORTANT: 不要把带有 tool_calls 但尚未执行的 assistant 消息写回到会话里。
        // OpenAI 要求：一旦历史中出现带 tool_calls 的 assistant 消息，下一条必须是对应的 tool 消息。
        // 这里我们只是让模型做“决策”，不会真正执行这些 tool_calls，所以必须丢弃它们，只保留文字内容。
        _session.AddAssistant(lastResponse.Content, null);
        _session.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Content = """
                [System: 你已达到本轮工具调用轮数上限。请从以下三种中选择一种并回复：
                1) [DECISION:STOP] 然后直接给用户最终总结（不要再用工具）。
                2) [DECISION:CONTINUE] 然后继续发起你需要的工具调用（我们会再执行一轮后结束）。
                3) [DECISION:ASK_USER] 然后写一句简短提示，询问用户是否继续（例如：已执行较多轮工具调用，是否继续？）。
                """
        });

        var toolsForTurn = GetToolsForCurrentPhase();
        var toolDefs = toolsForTurn.All.Select(t => new ToolDefinition(t.Name, t.Description, t.Parameters)).ToList();
        var limitMsgs = BuildMessages(null);
        _runLogger?.WriteModelRound(-1, _phase.ToString(), limitMsgs.Count, ApproxChatChars(limitMsgs));
        ModelResponse decision;
        try
        {
            decision = await _model.ChatAsync(limitMsgs, toolDefs, contentProgress, ct);
        }
        catch (Exception ex)
        {
            _runLogger?.WriteError("model.ChatAsync(limit_decision)", ex);
            return new AgentTurnResult(false, $"Limit decision failed: {ex.Message}");
        }

        _runLogger?.WriteAssistantResponse(decision.Content, decision.ToolCalls, decision.Usage);

        var content = decision.Content ?? "";
        if (content.Contains("[DECISION:ASK_USER]", StringComparison.OrdinalIgnoreCase))
        {
            var msg = content;
            var idx = msg.IndexOf("[DECISION:ASK_USER]", StringComparison.OrdinalIgnoreCase);
            msg = msg[(idx + "[DECISION:ASK_USER]".Length)..].Trim();
            var lineEnd = msg.IndexOf('\n');
            if (lineEnd > 0) msg = msg[..lineEnd].Trim();
            if (string.IsNullOrWhiteSpace(msg)) msg = "已执行较多轮工具调用，是否继续？";
            _session.AddAssistant(decision.Content, null);
            return new AgentTurnResult(true, null, NeedsUserConfirmation: true, ConfirmMessage: msg);
        }

        if (content.Contains("[DECISION:CONTINUE]", StringComparison.OrdinalIgnoreCase) && decision.ToolCalls.Count > 0)
        {
            _session.AddAssistant(decision.Content, decision.ToolCalls);
            var limitResults = await ExecuteToolCallsAsync(decision.ToolCalls, ct);
            _session.AddToolResults(limitResults);
            _session.Add(new ChatMessage
            {
                Role = MessageRole.User,
                Content = "[System: 已执行最后一轮工具。请根据当前信息给用户一个简洁总结，不要再用工具。]"
            });
            try
            {
                var wrapMsgs = BuildMessages(null);
                _runLogger?.WriteModelRound(-2, _phase.ToString(), wrapMsgs.Count, ApproxChatChars(wrapMsgs));
                var wrap = await _model.ChatAsync(wrapMsgs, [], contentProgress, ct);
                _runLogger?.WriteAssistantResponse(wrap.Content, wrap.ToolCalls, wrap.Usage);
                _session.AddAssistant(wrap.Content, null);
                return new AgentTurnResult(true, null);
            }
            catch (Exception ex)
            {
                _runLogger?.WriteError("model.ChatAsync(post_continue_wrap)", ex);
                return new AgentTurnResult(false, $"Post-continue summary failed: {ex.Message}");
            }
        }

        // [DECISION:STOP] or no tag: add assistant reply and end
        _session.AddAssistant(decision.Content, null);
        return new AgentTurnResult(true, null);
    }

    /// <summary>When limit hit with pending tool calls, request a summary reply with no tools.</summary>
    private async Task<AgentTurnResult> ForceWrapUpAsync(
        IProgress<string>? contentProgress,
        CancellationToken ct)
    {
        // Don't add the assistant's tool_calls to session (no tool results). Inject wrap-up instead.
        _session.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Content = "[System: 本轮工具调用次数已达上限。请根据目前已有的信息，给用户一个简洁的总结和回答。不要再调用任何工具。下次用户发送新消息时，工具调用额度会重新恢复。]"
        });

        var wrapMessages = BuildMessages(warningTurnsRemaining: null);
        _runLogger?.WriteModelRound(-3, _phase.ToString(), wrapMessages.Count, ApproxChatChars(wrapMessages));
        try
        {
            var wrap = await _model.ChatAsync(wrapMessages, [], contentProgress, ct);
            _runLogger?.WriteAssistantResponse(wrap.Content, wrap.ToolCalls, wrap.Usage);
            _session.AddAssistant(wrap.Content, null);
            return new AgentTurnResult(true, null);
        }
        catch (Exception ex)
        {
            _runLogger?.WriteError("model.ChatAsync(force_wrap_up)", ex);
            return new AgentTurnResult(false, $"Limit reached. Summarization failed: {ex.Message}");
        }
    }

    private List<ChatMessage> BuildMessages(int? warningTurnsRemaining)
    {
        var sys = _systemPrompt;

        if (_todoStore is { HasItems: true })
            sys += _todoStore.FormatForPrompt();

        if (_planStore is { HasPlan: true })
            sys += _planStore.FormatForPrompt();

        if (_workflow.Enabled)
        {
            sys += "\n\n[Workflow] ";
            sys += _phase switch
            {
                WorkflowPhase.Observe =>
                    BuildObservePhaseMessage(),
                WorkflowPhase.ActPrep =>
                    "Phase=ACT_PREP (one round only, no tools). Output a <thinking>...</thinking> block (short or long): what you will do in Act, risks, and file/command scope. " +
                    "Do not call tools this turn; the next turn will unlock Act.",
                WorkflowPhase.Act => "Phase=ACT. You may edit files (prefer str_replace for small precise edits) and use exec when appropriate.",
                WorkflowPhase.Verify => "Phase=VERIFY. Re-check with read/grep/glob; use exec for build/tests/skills; avoid further edits unless necessary.",
                _ => "Phase=ACT."
            };
        }

        if (warningTurnsRemaining is { } n && n <= 3)
        {
            sys += $"\n\n[Important: 剩余{n}轮工具调用。如已有足够信息，请优先总结并回答用户。]";
        }

        var list = new List<ChatMessage>
        {
            new() { Role = MessageRole.System, Content = sys }
        };
        var limit = _maxFailedToolResultChars;
        foreach (var m in _session.Messages)
        {
            if (m.Role == MessageRole.Tool && m.IsToolError && limit is { } L && L > 0)
            {
                var content = m.Content ?? "";
                if (content.Length > L)
                    list.Add(new ChatMessage { Role = m.Role, Content = content[..L] + "\n[...]", ToolCallId = m.ToolCallId, IsToolError = true });
                else
                    list.Add(m);
            }
            else
                list.Add(m);
        }
        return list;
    }

    /// <summary>Tools considered safe to run in parallel (read-only, no side effects).</summary>
    private static readonly HashSet<string> ReadLikeTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "read", "read_many", "list_dir", "grep", "glob", "memory_get", "memory_search"
    };

    /// <summary>
    /// Execute tool calls with parallelism: read-like tools run concurrently;
    /// write-like/exec/spawn tools run sequentially to avoid conflicts.
    /// Order of results matches order of tool calls.
    /// </summary>
    private async Task<List<ToolResult>> ExecuteToolCallsAsync(IReadOnlyList<ToolCall> toolCalls, CancellationToken ct)
    {
        if (toolCalls.Count <= 1)
        {
            var singleResults = new List<ToolResult>(1);
            foreach (var tc in toolCalls)
            {
                _onToolStarting?.Invoke(tc.Name, tc.Arguments);
                singleResults.Add(await ExecuteToolAsync(tc, ct));
            }
            return singleResults;
        }

        // Mixed batch: run read-like tools in parallel when safe, but preserve overall ordering.
        // Rule: If a tool earlier in the list can have side effects (non-read-like), we do not
        // run any later tools ahead of it.
        //
        // This keeps behavior predictable while still speeding up common patterns like:
        //   grep/read/list_dir ... then write/exec.
        var results = new ToolResult[toolCalls.Count];
        var i = 0;
        while (i < toolCalls.Count)
        {
            if (!ReadLikeTools.Contains(toolCalls[i].Name))
            {
                var tc = toolCalls[i];
                _onToolStarting?.Invoke(tc.Name, tc.Arguments);
                results[i] = await ExecuteToolAsync(tc, ct);
                i++;
                continue;
            }

            // Batch consecutive read-like calls.
            var start = i;
            while (i < toolCalls.Count && ReadLikeTools.Contains(toolCalls[i].Name))
                i++;
            var end = i; // exclusive

            for (var j = start; j < end; j++)
                _onToolStarting?.Invoke(toolCalls[j].Name, toolCalls[j].Arguments);

            var tasks = new Task<ToolResult>[end - start];
            for (var j = start; j < end; j++)
            {
                var index = j;
                tasks[index - start] = ExecuteToolAsync(toolCalls[index], ct);
            }

            var completed = await Task.WhenAll(tasks);
            for (var k = 0; k < completed.Length; k++)
                results[start + k] = completed[k];
        }

        return [.. results];
    }

    private async Task<ToolResult> ExecuteToolAsync(ToolCall tc, CancellationToken ct)
    {
        // DeepSeek (and some providers) may return empty string for tool args; normalize to "{}"
        var argsJson = string.IsNullOrWhiteSpace(tc.Arguments) ? "{}" : tc.Arguments;
        ToolResult? res = null;
        string? logBody = null;
        _runLogger?.WriteToolStarting(tc.Name, argsJson);
        try
        {
            if (!IsToolAllowedInCurrentPhase(tc.Name))
            {
                res = new ToolResult
                {
                    ToolCallId = tc.Id,
                    Content = $"Error: tool '{tc.Name}' is not allowed in workflow phase '{_phase}'. " +
                              "In Observe, use read/search tools; exec may be allowed without a plan when the host enables it; " +
                              "file edits need a plan or TODOs before Act when plan-gating is on.",
                    IsError = true
                };
                logBody = res.Content;
                return res;
            }

            var tool = _toolsBase.Get(tc.Name);
            if (tool is null)
            {
                res = new ToolResult { ToolCallId = tc.Id, Content = $"Error: unknown tool '{tc.Name}'", IsError = true };
                logBody = res.Content;
                return res;
            }

            Dictionary<string, object?> args;
            try
            {
                using var doc = JsonDocument.Parse(argsJson);
                args = new Dictionary<string, object?>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                    args[prop.Name] = prop.Value.Clone();
            }
            catch (JsonException)
            {
                args = new Dictionary<string, object?>();
            }
            catch
            {
                args = new Dictionary<string, object?>();
            }

            try
            {
                var result = await tool.ExecuteAsync(args, ct);
                var raw = result ?? string.Empty;
                logBody = raw;
                var content = raw;
                // sessions_spawn 的返回是子 agent 的完整回复，不截断，确保主 agent 能完整看到子 agent 的结论，避免重复派发任务
                if (tc.Name != "sessions_spawn" && _maxToolResultChars is { } limit && limit > 0 && content.Length > limit)
                {
                    content = content[..limit] + "\n[truncated]";
                }

                res = new ToolResult { ToolCallId = tc.Id, Content = content, IsError = false };
                return res;
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"[Tool] {tc.Name} error: {ex.GetType().Name}: {ex.Message}");
                res = new ToolResult
                {
                    ToolCallId = tc.Id,
                    Content = $"Error: {ex.Message}",
                    IsError = true
                };
                logBody = res.Content;
                return res;
            }
        }
        finally
        {
            if (res is not null)
            {
                var content = res.Content ?? "";
                var uiSuccess = !res.IsError
                                && !content.TrimStart().StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                                && !content.TrimStart().StartsWith("错误", StringComparison.Ordinal);
                _onToolCompleted?.Invoke(tc.Name, argsJson, uiSuccess);
                _runLogger?.WriteToolCompleted(tc.Name, argsJson, uiSuccess, logBody ?? content);
            }

            if (_workflow.Enabled && _workflow.AutoVerifyAfterFirstWrite && _phase == WorkflowPhase.Act)
            {
                if (string.Equals(tc.Name, "write", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tc.Name, "str_replace", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tc.Name, "text_edit", StringComparison.OrdinalIgnoreCase))
                {
                    _phase = WorkflowPhase.Verify;
                }
            }
        }
    }

    private void ResetWorkflowForNewUserMessage()
    {
        if (!_workflow.Enabled)
        {
            _phase = WorkflowPhase.Act;
            return;
        }

        // If gating is off, start directly in Act.
        _phase = _workflow.RequirePlanForWrite ? WorkflowPhase.Observe : WorkflowPhase.Act;
        _planStore?.Clear();
    }

    private string BuildObservePhaseMessage()
    {
        var core =
            "Phase=OBSERVE. Search/read/list; browser and delegation tools if available. ";
        var execLine = _workflow.AllowExecInObserve
            ? "Exec is allowed here for one-off commands without a prior plan. "
            : "No exec until Act. ";
        var writes =
            "Writes (write/str_replace/text_edit) need Act when plan-gating is on—submit a short plan or TODOs first. ";
        return core + execLine + writes;
    }

    private void MaybePromoteObserveToAct()
    {
        if (!_workflow.Enabled) return;
        if (_phase != WorkflowPhase.Observe) return;
        if (!_workflow.RequirePlanForWrite)
        {
            _phase = WorkflowPhase.Act;
            return;
        }

        var hasPlan = _planStore is { HasPlan: true };
        var hasTodos = _todoStore is { HasItems: true };
        if (hasPlan || hasTodos)
            _phase = _workflow.RequireThinkingBeforeAct ? WorkflowPhase.ActPrep : WorkflowPhase.Act;
    }

    private IToolRegistry GetToolsForCurrentPhase()
    {
        if (!_workflow.Enabled) return _toolsBase;

        // Minimal per-phase allowlist, intersected with base tool policy.
        var allow = _phase switch
        {
            WorkflowPhase.Observe => BuildObservePhaseAllowlist(),
            WorkflowPhase.ActPrep => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            WorkflowPhase.Verify => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "grep", "glob", "read", "read_many", "list_dir", "memory_get", "memory_search", "todo", "exec"
            },
            _ => null
        };

        if (allow is null) return _toolsBase;
        return new PhaseToolFilter(_toolsBase, allow);
    }

    private bool IsToolAllowedInCurrentPhase(string toolName)
    {
        if (!_workflow.Enabled) return true;

        // Always honor per-phase filter.
        var toolsForTurn = GetToolsForCurrentPhase();
        return toolsForTurn.Get(toolName) is not null;
    }

    private sealed class PhaseToolFilter : IToolRegistry
    {
        private readonly IToolRegistry _inner;
        private readonly HashSet<string> _allowed;

        public PhaseToolFilter(IToolRegistry inner, HashSet<string> allowed)
        {
            _inner = inner;
            _allowed = allowed;
        }

        public IReadOnlyList<ITool> All => _inner.All.Where(t => _allowed.Contains(t.Name)).ToList();

        public ITool? Get(string name)
        {
            if (!_allowed.Contains(name)) return null;
            return _inner.Get(name);
        }
    }
}
