using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using OpenLum.Console.Interfaces;
using OpenLum.Console.Json;
using OpenLum.Console.Models;

namespace OpenLum.Console.Providers;

/// <summary>
/// OpenAI Chat Completions API provider.
/// </summary>
public sealed class OpenAIModelProvider : IModelProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly bool? _think;

    public OpenAIModelProvider(string apiKey, string model = "gpt-4o-mini", string? baseUrl = null, bool? think = null)
    {
        var url = baseUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(url))
            url = "https://api.openai.com/v1";
        if (!url.EndsWith("/", StringComparison.Ordinal))
            url += "/";

        _http = new HttpClient
        {
            BaseAddress = new Uri(url),
            DefaultRequestHeaders = { { "Authorization", $"Bearer {apiKey}" } }
        };
        _model = model;
        _think = think;
    }

    public async Task<ModelResponse> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        IProgress<string>? contentProgress,
        CancellationToken ct = default)
    {
        var apiMessages = ToApiMessages(messages);
        var apiTools = ToApiTools(tools);

        if (contentProgress is not null)
        {
            return await StreamChatAsync(apiMessages, apiTools, contentProgress, ct);
        }

        var request = new OpenAIChatRequest
        {
            Model = _model,
            Messages = apiMessages,
            Tools = apiTools.Count > 0 ? apiTools : null,
            Stream = false,
            Think = _think
        };

        var reqBytes = JsonSerializer.SerializeToUtf8Bytes(request, OpenLumJsonContext.Default.OpenAIChatRequest);
        using var reqContent = new ByteArrayContent(reqBytes);
        reqContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var resp = await _http.PostAsync("chat/completions", reqContent, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"API error {resp.StatusCode}: {errBody}");
        }

        var bodyBytes = await resp.Content.ReadAsByteArrayAsync(ct);
        var body = JsonSerializer.Deserialize(bodyBytes, OpenLumJsonContext.Default.OpenAICompletionResponse)
            ?? throw new InvalidOperationException("Empty response");
        var choice = body.Choices?.FirstOrDefault()
            ?? throw new InvalidOperationException("No choices in response");

        return new ModelResponse(
            choice.Message?.Content ?? "",
            choice.Message?.ToolCalls?.Select(t => new ToolCall
            {
                Id = t.Id ?? "",
                Name = t.Function?.Name ?? "",
                Arguments = t.Function?.Arguments ?? "{}"
            }).ToList() ?? [],
            MapUsage(body.Usage));
    }

    private static ModelTokenUsage? MapUsage(OpenAIUsageDto? u)
    {
        if (u is null) return null;
        return new ModelTokenUsage(u.PromptTokens, u.CompletionTokens, u.TotalTokens);
    }

    private async Task<ModelResponse> StreamChatAsync(
        List<OpenAIMessageDto> apiMessages,
        List<OpenAIToolDto> apiTools,
        IProgress<string> progress,
        CancellationToken ct)
    {
        var request = new OpenAIChatRequest
        {
            Model = _model,
            Messages = apiMessages,
            Tools = apiTools.Count > 0 ? apiTools : null,
            Stream = true,
            Think = _think
        };

        var reqBytes = JsonSerializer.SerializeToUtf8Bytes(request, OpenLumJsonContext.Default.OpenAIChatRequest);
        using var reqContent = new ByteArrayContent(reqBytes);
        reqContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = reqContent };
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"API error {resp.StatusCode}: {errBody}");
        }

        var content = new StringBuilder();
        var toolCallsByIndex = new List<(string Id, string Name, StringBuilder Args)>();
        OpenAIUsageDto? streamUsage = null;

        await foreach (var line in ReadLinesAsync(resp.Content.ReadAsStreamAsync(ct), ct))
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var data = line["data: ".Length..].Trim();
                if (data.Length == 0) continue;
                if (data == "[DONE]") break;

                OpenAIStreamChunk? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize(data, OpenLumJsonContext.Default.OpenAIStreamChunk);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (chunk?.Usage is { } usageChunk)
                    streamUsage = usageChunk;

                var choice = chunk?.Choices?.FirstOrDefault(c => c.Index == 0) ?? chunk?.Choices?.FirstOrDefault();
                var delta = choice?.Delta;
                if (delta?.Content is { } c)
                {
                    content.Append(c);
                    progress.Report(c);
                }
                if (delta?.ToolCalls is { } tcList)
                {
                    foreach (var tc in tcList)
                    {
                        var idx = tc.Index;
                        if (idx < 0) continue;
                        while (toolCallsByIndex.Count <= idx)
                            toolCallsByIndex.Add(("", "", new StringBuilder()));
                        var slot = toolCallsByIndex[idx];
                        if (tc.Id is { } id)
                            slot.Id = id;
                        if (tc.Function?.Name is { } name)
                            slot.Name = name;
                        if (tc.Function?.Arguments is { } args)
                            slot.Args.Append(args);
                        toolCallsByIndex[idx] = slot;
                    }
                }
            }
        }

        var toolCallList = toolCallsByIndex
            .Where(s => !string.IsNullOrEmpty(s.Id))
            .Select(s => new ToolCall
            {
                Id = s.Id,
                Name = string.IsNullOrEmpty(s.Name) ? "" : s.Name,
                Arguments = s.Args.Length > 0 ? s.Args.ToString() : "{}"
            })
            .ToList();

        var fullContent = content.ToString();
        return new ModelResponse(fullContent, toolCallList, MapUsage(streamUsage));
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        Task<Stream> streamTask,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await streamTask;
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
            yield return line;
    }

    private static List<OpenAIMessageDto> ToApiMessages(IReadOnlyList<ChatMessage> messages)
    {
        var list = new List<OpenAIMessageDto>();
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case MessageRole.System:
                    list.Add(new OpenAIMessageDto { Role = "system", Content = m.Content });
                    break;
                case MessageRole.User:
                    list.Add(new OpenAIMessageDto { Role = "user", Content = m.Content });
                    break;
                case MessageRole.Assistant:
                    if (m.ToolCalls is { Count: > 0 } tc)
                    {
                        list.Add(new OpenAIMessageDto
                        {
                            Role = "assistant",
                            Content = m.Content ?? "",
                            ToolCalls = tc.Select(t => new OpenAIToolCallRequestDto
                            {
                                Id = t.Id,
                                Type = "function",
                                Function = new OpenAIToolCallFunctionRequestDto
                                {
                                    Name = t.Name,
                                    Arguments = t.Arguments
                                }
                            }).ToList()
                        });
                    }
                    else
                    {
                        list.Add(new OpenAIMessageDto { Role = "assistant", Content = m.Content ?? "" });
                    }
                    break;
                case MessageRole.Tool:
                    if (m.ToolCallId is { } tid)
                        list.Add(new OpenAIMessageDto { Role = "tool", ToolCallId = tid, Content = m.Content });
                    break;
            }
        }
        return list;
    }

    private static List<OpenAIToolDto> ToApiTools(IReadOnlyList<ToolDefinition> tools)
    {
        var list = new List<OpenAIToolDto>();
        foreach (var t in tools)
        {
            list.Add(new OpenAIToolDto
            {
                Type = "function",
                Function = new OpenAIToolFunctionDto
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = new OpenAIParametersDto
                    {
                        Type = "object",
                        Properties = t.Parameters.ToDictionary(
                            p => p.Name,
                            p => new OpenAIPropDto { Type = p.Type, Description = p.Description }),
                        Required = t.Parameters.Where(p => p.Required).Select(p => p.Name).ToList()
                    }
                }
            });
        }
        return list;
    }
}
