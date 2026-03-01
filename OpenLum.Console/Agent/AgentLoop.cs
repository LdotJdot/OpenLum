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

    public AgentLoop(
        IModelProvider model,
        IToolRegistry tools,
        ISession session,
        string systemPrompt,
        SessionCompactor? compactor = null,
        Action<string, string>? onToolExecuting = null)
    {
        _model = model;
        _tools = tools;
        _session = session;
        _systemPrompt = systemPrompt;
        _compactor = compactor;
        _onToolExecuting = onToolExecuting;
    }

    public async Task<AgentTurnResult> RunAsync(
        string userPrompt,
        IProgress<string>? contentProgress,
        CancellationToken ct = default)
    {
        _session.Add(new ChatMessage { Role = MessageRole.User, Content = userPrompt });

        const int maxTurns = 50;
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
                return new AgentTurnResult(false, ex.Message);
            }

            if (response.ToolCalls.Count == 0)
            {
                _session.AddAssistant(response.Content, null);
                return new AgentTurnResult(true, null);
            }

            if (turn == maxTurns - 1)
            {
                // Hit limit but model still wants to call tools. Force one final reply with no tools.
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
            return new ToolResult { ToolCallId = tc.Id, Content = result, IsError = false };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolCallId = tc.Id,
                Content = $"Error: {ex.Message}",
                IsError = true
            };
        }
    }
}
