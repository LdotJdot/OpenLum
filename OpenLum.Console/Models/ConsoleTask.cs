using System.Collections.Generic;

namespace OpenLum.Console.Models;

/// <summary>
/// Structured description of a console task for a single user turn.
/// This allows higher layers to reason about intent beyond raw text.
/// </summary>
public sealed record ConsoleTask(
    string TaskType,
    string TargetScope,
    IReadOnlyList<string> FocusAreas,
    string UserQuery);

