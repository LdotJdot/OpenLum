using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Tools;

/// <summary>
/// Allows the model to submit a lightweight plan artifact.
/// Hosts can optionally gate write-like tools behind a plan phase.
/// </summary>
public sealed class SubmitPlanTool : ITool
{
    private readonly PlanStore _store;
    private readonly Action<string>? _onPlanCommitted;

    public SubmitPlanTool(PlanStore store, Action<string>? onPlanCommitted = null)
    {
        _store = store;
        _onPlanCommitted = onPlanCommitted;
    }

    public string Name => "submit_plan";

    public string Description =>
        "Submit or update a short, actionable plan (steps, files, risks). " +
        "When workflow gates **file edits**, call this or **todo** in Observe to unlock Act for write/str_replace/text_edit. " +
        "Prefer calling **submit_plan alone** in that assistant turn (no other tool_calls in the same message) so the host can run ACT_PREP (thinking-only) next; if you batch more tools after submit_plan in the same message, those calls will error until ACT_PREP finishes. " +
        "append=true concatenates to the current plan. " +
        "Do not submit a plan only to run a quick exec—Observe often allows exec without a plan (see host workflow).";

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
            NotifyPlanCommitted();
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
        NotifyPlanCommitted();
        return Task.FromResult(_store.FormatAsResult());
    }

    private void NotifyPlanCommitted()
    {
        var text = _store.GetCurrentPlanText();
        if (!string.IsNullOrWhiteSpace(text))
            _onPlanCommitted?.Invoke(text);
    }
}
