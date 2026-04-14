using System.Text;

namespace OpenLum.Console.Tools;

/// <summary>
/// Stores a lightweight plan artifact for the current session/run.
/// Injected into the system prompt to make multi-step work more structured.
/// </summary>
public sealed class PlanStore
{
    private readonly object _lock = new();
    private string? _plan;

    public bool HasPlan
    {
        get { lock (_lock) return !string.IsNullOrWhiteSpace(_plan); }
    }

    public void Set(string? plan)
    {
        lock (_lock)
        {
            _plan = string.IsNullOrWhiteSpace(plan) ? null : plan.Trim();
        }
    }

    public void Clear()
    {
        lock (_lock) _plan = null;
    }

    /// <summary>Current plan body for host UI (e.g. console echo after submit_plan).</summary>
    public string GetCurrentPlanText()
    {
        lock (_lock)
        {
            return _plan ?? "";
        }
    }

    public string FormatForPrompt()
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(_plan)) return "";
            var sb = new StringBuilder();
            sb.AppendLine("\n[Active Plan:]");
            sb.AppendLine(_plan);
            return sb.ToString();
        }
    }

    public string FormatAsResult()
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(_plan)) return "Plan is empty.";
            return "Updated plan:\n" + _plan;
        }
    }
}

