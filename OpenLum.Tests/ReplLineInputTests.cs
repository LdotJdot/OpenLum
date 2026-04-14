using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenLum.Console.Hosting;
using Xunit;

namespace OpenLum.Tests;

public sealed class ReplLineInputTests
{
    [Fact]
    public void ReplLineInput_From_preserves_cjk_lines()
    {
        var lines = new Queue<string?>(new[] { "你好，世界", "混合 mixed 文本", "/exit" });
        var input = ReplLineInput.From(() => lines.Count > 0 ? lines.Dequeue() : null);

        Assert.Equal("你好，世界", input.ReadLine());
        Assert.Equal("混合 mixed 文本", input.ReadLine());
        Assert.Equal("/exit", input.ReadLine());
        Assert.Null(input.ReadLine());
    }

    [Fact]
    public void ReplLineInput_From_utf8_file_simulation_preserves_cjk()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var path = Path.Combine(Path.GetTempPath(), "openlum-repl-utf8-test-" + Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllText(path, "第一行：中文\n第二行：確認\n", utf8);

            using var reader = new StreamReader(path, utf8, detectEncodingFromByteOrderMarks: true);
            var input = ReplLineInput.From(reader.ReadLine);

            Assert.Equal("第一行：中文", input.ReadLine());
            Assert.Equal("第二行：確認", input.ReadLine());
            Assert.Null(input.ReadLine());
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                /* ignore */
            }
        }
    }

}
