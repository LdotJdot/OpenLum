using System.Runtime.InteropServices;
using System.Text;

namespace OpenLum.Console.Hosting;

/// <summary>Console host implementation for line input with UTF-8 initialization (incl. Windows CP 65001).</summary>
public sealed class ConsoleReplLineInput : IReplLineInput
{    
    public string? ReadLine() => System.Console.ReadLine();
  
}
