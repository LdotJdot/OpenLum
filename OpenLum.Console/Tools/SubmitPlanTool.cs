using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Tools;

/// <summary>
/// Allows the model to submit a lightweight plan artifact.
/// Hosts can optionally gate write-like tools behind a plan phase.
/// </summary>
public sealed class SubmitPlanTool : ITool
{
    private readonly PlanStore _store;

    public SubmitPlanTool(PlanStore store) => _store = store;

    public string Name => "submit_plan";

    public string Description =>
        "Submit or update a lightweight plan artifact for multi-step tasks. " +
        "Use this before making write-like changes when the workflow requires a plan.";

    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("content", "string", "Plan content (short, actionable steps).", true),
        new ToolParameter("append", "boolean", "If true, append to existing plan (default false).", false)
    ];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var content = args.GetValueOrDefault("content")?.ToString();
        if (string.IsNullOrWhiteSpace(content))
            return Task.FromResult("Error: content is required.");

        var append = ToolArgHelpers.ParseBool(args, "append");
        if (!append)
        {
            _store.Set(content);
            return Task.FromResult(_store.FormatAsResult());
        }

        // Append
        var existing = _store.FormatForPrompt();
        var normalizedExisting = existing
            .Replace("\n[Active Plan:]\n", "", StringComparison.Ordinal)
            .Trim();
        var combined = string.IsNullOrWhiteSpace(normalizedExisting)
            ? content.Trim()
            : normalizedExisting + "\n" + content.Trim();
        _store.Set(combined);
        return Task.FromResult(_store.FormatAsResult());
    }
}

