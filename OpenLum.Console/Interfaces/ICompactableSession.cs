using OpenLum.Console.Models;

namespace OpenLum.Console.Interfaces;

/// <summary>
/// Session that supports compaction (replace older messages with a summary).
/// </summary>
public interface ICompactableSession : ISession
{
    int MessageCount { get; }
    IReadOnlyList<ChatMessage> GetMessagesToCompact(int reserveRecent);
    void CompactWithSummary(int reserveRecent, string summary);
}
