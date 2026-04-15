using System.Text.Json;
using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Tools;

/// <summary>
/// Manages a structured task list for the agent's working memory.
/// Supports merge (update by id) and replace (full swap) modes.
/// </summary>
public sealed class TodoTool : ITool
{
    private readonly TodoStore _store;

    public TodoTool(TodoStore store) => _store = store;

    public string Name => "todo";

    public string Description =>
        "Create or update a structured TODO list for tracking multi-step tasks. " +
        "Each item has id, content, status (pending|in_progress|completed|cancelled; common aliases like done→completed are accepted). " +
        "merge=true: upsert by id; merge=false: replace the whole list. " +
        "When workflow gates writes, TODOs in Observe unlock Act (same role as submit_plan).";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("todos", "array",
            "Array of {id, content, status} objects. status: pending|in_progress|completed|cancelled.", true),
        new ToolParameter("merge", "boolean",
            "true: merge into existing by id. false: replace entire list (default false).", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var merge = ToolArgHelpers.ParseBool(args, "merge");
        var items = ParseTodos(args);

        if (items.Count == 0)
            return Task.FromResult("Error: todos array is required with at least one item.");

        if (merge)
            _store.Merge(items);
        else
            _store.Replace(items);

        return Task.FromResult(_store.FormatAsResult());
    }

    private static List<TodoItem> ParseTodos(IReadOnlyDictionary<string, object?> args)
    {
        if (!args.TryGetValue("todos", out var val) || val is null)
            return [];

        var items = new List<TodoItem>();

        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in je.EnumerateArray())
                {
                    var item = ParseSingleTodo(elem);
                    if (item is not null) items.Add(item);
                }
            }
        }

        return items;
    }

    private static TodoItem? ParseSingleTodo(JsonElement elem)
    {
        if (elem.ValueKind != JsonValueKind.Object) return null;

        var id = elem.TryGetProperty("id", out var idProp) ? ToolArgHelpers.JsonElementAsString(idProp) : null;
        var content = elem.TryGetProperty("content", out var contentProp) ? ToolArgHelpers.JsonElementAsString(contentProp) : null;
        var status = elem.TryGetProperty("status", out var statusProp) ? ToolArgHelpers.JsonElementAsString(statusProp) : null;

        if (string.IsNullOrWhiteSpace(id)) return null;

        status = NormalizeStatus(status);

        return new TodoItem(id!, content ?? "", status ?? "pending");
    }

    private static string? NormalizeStatus(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim().ToLowerInvariant() switch
        {
            "pending" or "todo" => "pending",
            "in_progress" or "inprogress" or "working" => "in_progress",
            "completed" or "done" or "finished" => "completed",
            "cancelled" or "canceled" or "skipped" => "cancelled",
            _ => s.Trim().ToLowerInvariant()
        };
    }
}
