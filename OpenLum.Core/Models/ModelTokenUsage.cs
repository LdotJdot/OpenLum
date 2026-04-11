namespace OpenLum.Console.Models;

/// <summary>Token counts from the provider when available; otherwise null fields.</summary>
public sealed record ModelTokenUsage(
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);
