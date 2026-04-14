namespace OpenLum.Console.Hosting;

/// <summary>Supplies one line of user input for the REPL (host may map to console, pipe, RPC, etc.).</summary>
public interface IReplLineInput
{
    string? ReadLine();
}
