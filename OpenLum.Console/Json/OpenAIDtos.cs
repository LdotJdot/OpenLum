using System.Text.Json.Serialization;

namespace OpenLum.Console.Json;

/// <summary>
/// DTOs for OpenAI Chat Completions API. Used with JsonSerializerContext for AOT.
/// </summary>

// Response types
internal sealed class OpenAICompletionResponse
{
    [JsonPropertyName("choices")]
    public List<OpenAIChoice>? Choices { get; set; }
}

internal sealed class OpenAIChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public OpenAIMessage? Message { get; set; }

    [JsonPropertyName("delta")]
    public OpenAIStreamDelta? Delta { get; set; }
}

internal sealed class OpenAIMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAIToolCall>? ToolCalls { get; set; }
}

internal sealed class OpenAIToolCall
{
    [JsonPropertyName("index")]
    public int Index { get; set; } = -1;

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("function")]
    public OpenAIToolCallFunction? Function { get; set; }
}

internal sealed class OpenAIToolCallFunction
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

internal sealed class OpenAIStreamChunk
{
    [JsonPropertyName("choices")]
    public List<OpenAIChoice>? Choices { get; set; }
}

internal sealed class OpenAIStreamDelta
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAIToolCall>? ToolCalls { get; set; }
}

// Request types
internal sealed class OpenAIChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("messages")]
    public List<OpenAIMessageDto> Messages { get; set; } = [];

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAIToolDto>? Tools { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

internal sealed class OpenAIMessageDto
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAIToolCallRequestDto>? ToolCalls { get; set; }
}

internal sealed class OpenAIToolCallRequestDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAIToolCallFunctionRequestDto Function { get; set; } = new();
}

internal sealed class OpenAIToolCallFunctionRequestDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "";
}

internal sealed class OpenAIToolDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAIToolFunctionDto Function { get; set; } = new();
}

internal sealed class OpenAIToolFunctionDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public OpenAIParametersDto Parameters { get; set; } = new();
}

internal sealed class OpenAIParametersDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, OpenAIPropDto> Properties { get; set; } = [];

    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = [];
}

internal sealed class OpenAIPropDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
