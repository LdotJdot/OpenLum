using OpenLum.Console.Config;
using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Tools;

/// <summary>
/// Wraps an IToolRegistry and filters tools based on policy (profile + allow/deny).
/// </summary>
public sealed class ToolPolicyFilter : IToolRegistry
{
    private readonly IToolRegistry _inner;
    private readonly HashSet<string> _allowed;
    private readonly HashSet<string> _denied;

    public ToolPolicyFilter(IToolRegistry inner, ToolPolicyConfig policy)
    {
        _inner = inner;

        var profileList = ToolProfiles.ProfileAllowlists
            .GetValueOrDefault(ToolProfiles.Normalize(policy.Profile), ["group:fs"]);
        var fromProfile = ToolProfiles.Expand(profileList);
        var fromAllow = ToolProfiles.Expand(policy.Allow);
        var fromDeny = ToolProfiles.Expand(policy.Deny);

        _allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (policy.Allow.Count > 0)
        {
            _allowed.UnionWith(fromAllow);
        }
        else
        {
            _allowed.UnionWith(fromProfile);
            _allowed.UnionWith(fromAllow);
        }

        _denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in fromDeny)
        {
            if (d == "*")
            {
                _allowed.Clear();
                break;
            }
            _denied.Add(d);
        }
    }

    public IReadOnlyList<ITool> All
    {
        get
        {
            var list = _inner.All;
            if (_denied.Contains("*"))
                return [];
            return list.Where(t => IsAllowed(t.Name)).ToList();
        }
    }

    public ITool? Get(string name)
    {
        if (!IsAllowed(name))
            return null;
        return _inner.Get(name);
    }

    private bool IsAllowed(string name)
    {
        var n = ToolProfiles.Normalize(name);
        if (_denied.Contains(n))
            return false;
        if (_allowed.Count == 0)
            return true;
        return _allowed.Contains(n);
    }
}
