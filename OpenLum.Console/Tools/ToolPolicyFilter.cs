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
    private readonly bool _denyAll;

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
        var denyAll = false;
        foreach (var d in fromDeny)
        {
            if (d == "*")
            {
                denyAll = true;
                continue;
            }
            _denied.Add(d);
        }

        _denyAll = denyAll;
        if (_denyAll)
        {
            // When deny-all is requested, ignore any allow list for safety.
            _allowed.Clear();
        }
    }

    public IReadOnlyList<ITool> All
    {
        get
        {
            var list = _inner.All;
            if (_denyAll)
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
        if (_denyAll)
            return false;

        var n = ToolProfiles.Normalize(name);
        if (_denied.Contains(n))
            return false;
        if (_allowed.Count == 0)
            return true;
        return _allowed.Contains(n);
    }
}
