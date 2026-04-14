using System.Text.Json.Serialization;

namespace OpenLum.Console.Session;

/// <summary>Native AOT / trim-safe JSON for .openlum files.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConversationRecordFileDto))]
[JsonSerializable(typeof(MessageFileDto))]
[JsonSerializable(typeof(ToolCallFileDto))]
[JsonSerializable(typeof(TodoItemFileDto))]
[JsonSerializable(typeof(List<MessageFileDto>))]
[JsonSerializable(typeof(List<ToolCallFileDto>))]
[JsonSerializable(typeof(List<TodoItemFileDto>))]
internal partial class ConversationRecordJsonContext : JsonSerializerContext;
