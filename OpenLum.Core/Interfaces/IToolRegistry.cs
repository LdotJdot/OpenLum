namespace OpenLum.Console.Interfaces;

/// <summary>
/// Resolves tools by name.
/// </summary>
public interface IToolRegistry
{
    IReadOnlyList<ITool> All { get; }
    ITool? Get(string name);
}
