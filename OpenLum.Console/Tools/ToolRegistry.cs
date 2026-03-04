using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Tools;

/// <summary>
/// Simple registry that holds all tools and resolves by name.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _byName = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ITool> All => _byName.Values.ToList();

    public ITool? Get(string name) => _byName.GetValueOrDefault(name);

    public void Register(ITool tool)
    {
        _byName[tool.Name] = tool;
    }
}
