using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OpenLum.Console.Tools;
using Xunit;

namespace OpenLum.Tests;

public class StrReplaceToolTests
{
    [Fact]
    public async Task ExecuteAsync_DecodesLiteralJsonStyleUnicodeInOldString_WhenFileUsesRealCharacters()
    {
        var dir = Path.Combine(Path.GetTempPath(), "openlum-strreplace-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "var x = \"<tag>\";");
            var tool = new StrReplaceTool(dir);
            var args = new Dictionary<string, object?>
            {
                ["path"] = "a.txt",
                // Six-char sequence \ u 0 0 3 c instead of '<' in the snippet (common model mistake).
                ["old_string"] = "\"\\u003ctag>\"",
                ["new_string"] = "\"<t>\""
            };
            var msg = await tool.ExecuteAsync(args, default);
            Assert.Contains("Replaced", msg, StringComparison.Ordinal);
            var text = await File.ReadAllTextAsync(Path.Combine(dir, "a.txt"));
            Assert.Equal("var x = \"<t>\";", text);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotCollapseDoubleBackslashBeforeUnicodeSequence()
    {
        // File keeps two literal backslashes before u003c; parity must not decode that to '<'.
        var dir = Path.Combine(Path.GetTempPath(), "openlum-strreplace-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "b.txt"), "see \\\\u003c for details");
            var tool = new StrReplaceTool(dir);
            var args = new Dictionary<string, object?>
            {
                ["path"] = "b.txt",
                ["old_string"] = "\\\\u003c",
                ["new_string"] = "OK"
            };
            var msg = await tool.ExecuteAsync(args, default);
            Assert.Contains("Replaced", msg, StringComparison.Ordinal);
            var text = await File.ReadAllTextAsync(Path.Combine(dir, "b.txt"));
            Assert.Equal("see OK for details", text);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
