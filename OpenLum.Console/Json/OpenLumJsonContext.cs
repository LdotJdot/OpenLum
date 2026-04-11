using System.Text.Json.Serialization;

namespace OpenLum.Console.Json;

/// <summary>
/// JsonSerializerContext for AOT-compatible serialization of OpenAI API types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OpenAICompletionResponse))]
[JsonSerializable(typeof(OpenAIUsageDto))]
[JsonSerializable(typeof(OpenAIChoice))]
[JsonSerializable(typeof(OpenAIMessage))]
[JsonSerializable(typeof(OpenAIToolCall))]
[JsonSerializable(typeof(OpenAIToolCallFunction))]
[JsonSerializable(typeof(OpenAIStreamChunk))]
[JsonSerializable(typeof(OpenAIStreamDelta))]
[JsonSerializable(typeof(OpenAIChatRequest))]
[JsonSerializable(typeof(OpenAIMessageDto))]
[JsonSerializable(typeof(OpenAIToolCallRequestDto))]
[JsonSerializable(typeof(OpenAIToolCallFunctionRequestDto))]
[JsonSerializable(typeof(OpenAIToolDto))]
[JsonSerializable(typeof(OpenAIToolFunctionDto))]
[JsonSerializable(typeof(OpenAIParametersDto))]
[JsonSerializable(typeof(OpenAIPropDto))]
[JsonSerializable(typeof(Dictionary<string, OpenAIPropDto>))]
internal sealed partial class OpenLumJsonContext : JsonSerializerContext
{
}
