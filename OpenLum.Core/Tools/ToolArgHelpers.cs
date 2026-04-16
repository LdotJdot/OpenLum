using System.Globalization;
using System.Text.Json;

namespace OpenLum.Console.Tools;

/// <summary>
/// Shared argument-parsing helpers for ITool implementations.
/// Handles JsonElement (from model JSON) and native CLR types uniformly.
/// </summary>
internal static class ToolArgHelpers
{
    /// <summary>
    /// Best-effort int coercion for a single JSON value (tool args or nested objects).
    /// Accepts JSON numbers, numeric strings, and common CLR numeric types.
    /// </summary>
    internal static bool TryParseJsonElementInt(JsonElement je, out int value)
    {
        value = 0;
        switch (je.ValueKind)
        {
            case JsonValueKind.Number:
                if (je.TryGetInt32(out value)) return true;
                if (je.TryGetInt64(out var l64))
                {
                    value = l64 > int.MaxValue ? int.MaxValue : l64 < int.MinValue ? int.MinValue : (int)l64;
                    return true;
                }
                if (je.TryGetDouble(out var d))
                {
                    value = (int)d;
                    return true;
                }
                return false;
            case JsonValueKind.String:
                return int.TryParse(je.GetString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            default:
                return false;
        }
    }

    /// <summary>
    /// Coerces a tool argument value to int (models may emit numbers as JSON strings or doubles).
    /// </summary>
    internal static bool TryParseIntObject(object? val, out int n)
    {
        n = 0;
        if (val is null) return false;
        switch (val)
        {
            case int i:
                n = i;
                return true;
            case long l:
                n = l > int.MaxValue ? int.MaxValue : l < int.MinValue ? int.MinValue : (int)l;
                return true;
            case short s:
                n = s;
                return true;
            case byte b:
                n = b;
                return true;
            case double d:
                n = (int)d;
                return true;
            case float f:
                n = (int)f;
                return true;
            case string s:
                return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
            case JsonElement je:
                return TryParseJsonElementInt(je, out n);
            default:
                return false;
        }
    }

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
        if (!TryParseIntObject(val, out var n)) return defaultVal;
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
            JsonElement je when je.ValueKind == JsonValueKind.String =>
                je.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false,
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var bi) => bi != 0,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    public static string? ParseString(IReadOnlyDictionary<string, object?> args, string key)
    {
        return args.GetValueOrDefault(key)?.ToString()?.Trim();
    }

    /// <summary>
    /// Reads a string from a JsonElement; accepts String or Number (models sometimes emit numeric ids).
    /// </summary>
    public static string? JsonElementAsString(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
