using System.Text.Json;

namespace OpenLum.Console.Tools;

/// <summary>
/// Shared argument-parsing helpers for ITool implementations.
/// Handles JsonElement (from model JSON) and native CLR types uniformly.
/// </summary>
internal static class ToolArgHelpers
{
    /// <summary>
    /// Parse an integer argument. Returns defaultVal if the key is absent or null.
    /// A value of 0 is treated as "use default" only if <paramref name="zeroMeansDefault"/> is true.
    /// </summary>
    public static int ParseInt(
        IReadOnlyDictionary<string, object?> args,
        string key,
        int defaultVal,
        int min = int.MinValue,
        int max = int.MaxValue,
        bool zeroMeansDefault = false)
    {
        if (!args.TryGetValue(key, out var val) || val is null) return defaultVal;
        var n = val switch
        {
            JsonElement je when je.TryGetInt32(out var i) => i,
            long l => (int)l,
            int i => i,
            double d => (int)d,
            _ => defaultVal
        };
        if (zeroMeansDefault && n == 0) return defaultVal;
        return Math.Clamp(n, min, max);
    }

    public static bool ParseBool(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var val) || val is null) return false;
        return val switch
        {
            bool b => b,
            JsonElement je when je.ValueKind == JsonValueKind.True => true,
            JsonElement je when je.ValueKind == JsonValueKind.False => false,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    public static string? ParseString(IReadOnlyDictionary<string, object?> args, string key)
    {
        return args.GetValueOrDefault(key)?.ToString()?.Trim();
    }
}
