using System.Collections.Generic;
using System.Text.Json;
using OpenLum.Console.Tools;
using Xunit;

namespace OpenLum.Tests;

public class ToolArgHelpersTests
{
    [Theory]
    [InlineData("42", 42)]
    [InlineData("\"99\"", 99)]
    [InlineData("100.0", 100)]
    public void ParseInt_AcceptsNumericJsonKinds(string json, int expected)
    {
        using var doc = JsonDocument.Parse($$"""{"k":{{json}}}""");
        var args = new Dictionary<string, object?> { ["k"] = doc.RootElement.GetProperty("k").Clone() };
        Assert.Equal(expected, ToolArgHelpers.ParseInt(args, "k", 0));
    }

    [Fact]
    public void ParseBool_AcceptsJsonStringTrue()
    {
        using var doc = JsonDocument.Parse("""{"b":"true"}""");
        var args = new Dictionary<string, object?> { ["b"] = doc.RootElement.GetProperty("b").Clone() };
        Assert.True(ToolArgHelpers.ParseBool(args, "b"));
    }
}
