using System.Text.Json;
using OpenLum.Console.Compaction;
using OpenLum.Console.Interfaces;
using OpenLum.Console.Models;

namespace OpenLum.Console.Agent;

/// <summary>
/// Runs the agent loop: user prompt → model → execute tools → model → ... → done.
/// </summary>
public sealed class AgentLoop : IAgent
{
    private readonly IModelProvider _model;
    private readonly IToolRegistry _tools;
    private readonly ISession _session;
    private readonly string _systemPrompt;
    private readonly SessionCompactor? _compactor;
    private readonly Action<string, string>? _onToolExecuting;
    private readonly int? _maxToolResultChars;
    private readonly int _maxToolTurns;
    private readonly bool _useModelDecideAtLimit;

    public AgentLoop(
        IModelProvider model,
        IToolRegistry tools,
        ISession session,
        string systemPrompt,
        SessionCompactor? compactor = null,
        Action<string, string>? onToolExecuting = null,
        int? maxToolResultChars = null,
        int maxToolTurns = 100,
        bool useModelDecideAtLimit = true)
    {
        _model = model;
        _tools = tools;
        _session = session;
        _systemPrompt = systemPrompt;
        _compactor = compactor;
        _onToolExecuting = onToolExecuting;
        _maxToolResultChars = maxToolResultChars;
        _maxToolTurns = Math.Max(1, maxToolTurns);
        _useModelDecideAtLimit = useModelDecideAtLimit;
    }

    public async Task<AgentTurnResult> RunAsync(
        string userPrompt,
        IProgress<string>? contentProgress,
        CancellationToken ct = default)
    {
        _session.Add(new ChatMessage { Role = MessageRole.User, Content = userPrompt });

        var maxTurns = _maxToolTurns;
        const int warningAt = 5; // Start warning this many turns before limit

        for (var turn = 0; turn < maxTurns; turn++)
        {
            if (_compactor is { } c && _session is ICompactableSession cs)
            {
                await c.CompactIfNeededAsync(cs, ct);
            }

            var remaining = maxTurns - turn;
            var messages = BuildMessages(remaining <= warningAt ? remaining : null);
            var toolDefs = _tools.All.Select(t => new ToolDefinition(
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
                return new AgentTurnResult(false, ex.Message);
            }

            if (response.ToolCalls.Count == 0)
            {
                _session.AddAssistant(response.Content, null);
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
            var results = new List<ToolResult>();
            foreach (var tc in response.ToolCalls)
            {
                _onToolExecuting?.Invoke(tc.Name, tc.Arguments);
                var result = await ExecuteToolAsync(tc, ct);
                results.Add(result);
            }
            _session.AddToolResults(results);
        }

        return new AgentTurnResult(false, "Max tool turns exceeded.");
    }

    /// <summary>When limit hit, ask model to choose: stop, continue (one more round), or ask user.</summary>
    private async Task<AgentTurnResult> DecideAtLimitAsync(
        ModelResponse lastResponse,
        IProgress<string>? contentProgress,
        CancellationToken ct)
    {
        _session.AddAssistant(lastResponse.Content, lastResponse.ToolCalls);
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

        var toolDefs = _tools.All.Select(t => new ToolDefinition(t.Name, t.Description, t.Parameters)).ToList();
        ModelResponse decision;
        try
        {
            decision = await _model.ChatAsync(BuildMessages(null), toolDefs, contentProgress, ct);
        }
        catch (Exception ex)
        {
            return new AgentTurnResult(false, $"Limit decision failed: {ex.Message}");
        }

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
            foreach (var tc in decision.ToolCalls)
            {
                _onToolExecuting?.Invoke(tc.Name, tc.Arguments);
                var result = await ExecuteToolAsync(tc, ct);
                _session.AddToolResults([result]);
            }
            _session.Add(new ChatMessage
            {
                Role = MessageRole.User,
                Content = "[System: 已执行最后一轮工具。请根据当前信息给用户一个简洁总结，不要再用工具。]"
            });
            try
            {
                var wrap = await _model.ChatAsync(BuildMessages(null), [], contentProgress, ct);
                _session.AddAssistant(wrap.Content, null);
                return new AgentTurnResult(true, null);
            }
            catch (Exception ex)
            {
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
        try
        {
            var wrap = await _model.ChatAsync(wrapMessages, [], contentProgress, ct);
            _session.AddAssistant(wrap.Content, null);
            return new AgentTurnResult(true, null);
        }
        catch (Exception ex)
        {
            return new AgentTurnResult(false, $"Limit reached. Summarization failed: {ex.Message}");
        }
    }

    private List<ChatMessage> BuildMessages(int? warningTurnsRemaining)
    {
        var sys = _systemPrompt;
        if (warningTurnsRemaining is { } n && n <= 3)
        {
            sys += $"\n\n[Important: 剩余{n}轮工具调用。如已有足够信息，请优先总结并回答用户。]";
        }

        var list = new List<ChatMessage>
        {
            new() { Role = MessageRole.System, Content = sys }
        };
        list.AddRange(_session.Messages);
        return list;
    }

    private async Task<ToolResult> ExecuteToolAsync(ToolCall tc, CancellationToken ct)
    {
        var tool = _tools.Get(tc.Name);
        if (tool is null)
        {
            return new ToolResult
            {
                ToolCallId = tc.Id,
                Content = $"Error: unknown tool '{tc.Name}'",
                IsError = true
            };
        }

        // DeepSeek (and some providers) may return empty string for tool args; normalize to "{}"
        var argsJson = string.IsNullOrWhiteSpace(tc.Arguments) ? "{}" : tc.Arguments;
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
            var content = result ?? string.Empty;
            if (_maxToolResultChars is { } limit && limit > 0 && content.Length > limit)
            {
                content = content[..limit];
            }

            return new ToolResult { ToolCallId = tc.Id, Content = content, IsError = false };
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"[Tool] {tc.Name} error: {ex.GetType().Name}: {ex.Message}");
            return new ToolResult
            {
                ToolCallId = tc.Id,
                Content = $"Error: {ex.Message}",
                IsError = true
            };
        }
    }
}
