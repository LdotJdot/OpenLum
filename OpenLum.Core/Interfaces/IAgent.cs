namespace OpenLum.Console.Interfaces;

/// <summary>
/// Result of a single agent turn.
/// </summary>
/// <param name="Success">Whether the turn completed successfully.</param>
/// <param name="ErrorMessage">Error message when not successful.</param>
/// <param name="NeedsUserConfirmation">When true, REPL should show ConfirmMessage and prompt user to continue or stop.</param>
/// <param name="ConfirmMessage">Message to show when NeedsUserConfirmation is true.</param>
public sealed record AgentTurnResult(
    bool Success,
    string? ErrorMessage,
    bool NeedsUserConfirmation = false,
    string? ConfirmMessage = null);

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

