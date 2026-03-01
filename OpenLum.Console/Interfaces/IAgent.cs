namespace OpenLum.Console.Interfaces;

/// <summary>
/// Result of a single agent turn.
/// </summary>
public sealed record AgentTurnResult(bool Success, string? ErrorMessage);

/// <summary>
/// Runs the agent loop: user prompt → model → tools → model → ... → final reply.
/// </summary>
public interface IAgent
{
    Task<AgentTurnResult> RunAsync(
        string userPrompt,
        IProgress<string>? contentProgress,
        CancellationToken ct = default);
}
