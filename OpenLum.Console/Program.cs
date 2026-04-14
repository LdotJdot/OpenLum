using OpenLum.Console.Hosting;
using System.Text;

static class Program
{
    static int Main(string[] args)
    {
  
            var lineInput = new ConsoleReplLineInput();
     

          return OpenLum.Console.Application.Run(args, lineInput);
    }
}
