using System.Text;

namespace OpenLum.Console.Tools;

public sealed record TodoItem(string Id, string Content, string Status);

/// <summary>
/// In-memory store for the agent's working todo list.
/// Lifetime is per-session (cleared when session clears).
/// The AgentLoop injects current state into context each turn.
/// </summary>
public sealed class TodoStore
{
    private List<TodoItem> _items = [];
    private readonly object _lock = new();

    public bool HasItems
    {
        get { lock (_lock) return _items.Count > 0; }
    }

    public void Replace(List<TodoItem> items)
    {
        lock (_lock) _items = [.. items];
    }

    public void Merge(List<TodoItem> updates)
    {
        lock (_lock)
        {
            foreach (var update in updates)
            {
                var idx = _items.FindIndex(i =>
                    string.Equals(i.Id, update.Id, StringComparison.OrdinalIgnoreCase));

                if (idx >= 0)
                {
                    var old = _items[idx];
                    _items[idx] = new TodoItem(
                        update.Id,
                        update.Content ?? old.Content,
                        update.Status ?? old.Status);
                }
                else
                {
                    _items.Add(update);
                }
            }
        }
    }

    public void Clear()
    {
        lock (_lock) _items.Clear();
    }

    /// <summary>
    /// Format current state for injection into system prompt.
    /// Returns empty string if no items.
    /// </summary>
    public string FormatForPrompt()
    {
        lock (_lock)
        {
            if (_items.Count == 0) return "";
            var sb = new StringBuilder();
            sb.AppendLine("\n[Active TODO list:]");
            foreach (var item in _items)
            {
                var icon = item.Status switch
                {
                    "completed" => "✓",
                    "in_progress" => "→",
                    "cancelled" => "✗",
                    _ => "○"
                };
                sb.AppendLine($"  {icon} [{item.Status}] {item.Content} (id: {item.Id})");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Format current state as tool result (more verbose).
    /// </summary>
    public string FormatAsResult()
    {
        lock (_lock)
        {
            if (_items.Count == 0)
                return "TODO list is empty.";
            var sb = new StringBuilder();
            sb.AppendLine("Updated TODO list:");
            foreach (var item in _items)
                sb.AppendLine($"- **{item.Status.ToUpperInvariant()}**: {item.Content} (id: {item.Id})");
            return sb.ToString();
        }
    }
}
