namespace OpenLum.Console.Hosting;

/// <summary>Factory helpers for <see cref="IReplLineInput"/>.</summary>
public static class ReplLineInput
{
    /// <summary>Adapts a delegate (e.g. <c>() => streamReader.ReadLine()</c>) to <see cref="IReplLineInput"/>.</summary>
    public static IReplLineInput From(Func<string?> read) => new FuncLineInput(read);

    private sealed class FuncLineInput : IReplLineInput
    {
        private readonly Func<string?> _read;

        public FuncLineInput(Func<string?> read) => _read = read;

        public string? ReadLine() => _read();
    }
}
