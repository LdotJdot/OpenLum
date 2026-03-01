using System.Text.Json;

namespace OpenLum.Console.Config;

/// <summary>
/// Loads app config from openlum.json (optional).
/// </summary>
public static class ConfigLoader
{
    private static readonly string[] SearchPaths =
    [
        "openlum.json",
        "openlum.console.json",
        "appsettings.json"
    ];

    public static AppConfig Load(string? configDir = null)
    {
        var baseDir = configDir ?? AppContext.BaseDirectory;
        foreach (var name in SearchPaths)
        {
            var path = Path.Combine(baseDir, name);
            if (!File.Exists(path))
                continue;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return new AppConfig
                {
                    ToolPolicy = ParseToolPolicy(root.TryGetProperty("tools", out var t) ? t : default),
                    Model = ParseModel(root.TryGetProperty("model", out var m) ? m : default),
                    Compaction = ParseCompaction(root.TryGetProperty("compaction", out var c) ? c : default),
                    Browser = ParseBrowser(root.TryGetProperty("browser", out var b) ? b : default),
                    Workspace = root.TryGetProperty("workspace", out var w) ? w.GetString()?.Trim() : null,
                    UserTimezone = root.TryGetProperty("userTimezone", out var tz) ? tz.GetString()?.Trim() : null
                };
            }
            catch
            {
                // Fall through to defaults
            }
        }

        return new AppConfig();
    }

    private static ToolPolicyConfig ParseToolPolicy(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return new ToolPolicyConfig();

        return new ToolPolicyConfig
        {
            Profile = el.TryGetProperty("profile", out var p) ? p.GetString() ?? "coding" : "coding",
            Allow = el.TryGetProperty("allow", out var a) ? ParseStringArray(a) : [],
            Deny = el.TryGetProperty("deny", out var d) ? ParseStringArray(d) : []
        };
    }

    private static ModelConfig ParseModel(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return new ModelConfig();

        return new ModelConfig
        {
            Provider = el.TryGetProperty("provider", out var p) ? p.GetString() ?? "openai" : "openai",
            Model = el.TryGetProperty("model", out var m) ? m.GetString() ?? "gpt-4o-mini" : "gpt-4o-mini",
            BaseUrl = el.TryGetProperty("baseUrl", out var u) ? u.GetString() : null,
            ApiKey = el.TryGetProperty("apiKey", out var k) ? k.GetString() : null
        };
    }

    private static CompactionConfig ParseCompaction(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return new CompactionConfig();

        return new CompactionConfig
        {
            Enabled = el.TryGetProperty("enabled", out var e) && e.GetBoolean(),
            MaxMessagesBeforeCompact = el.TryGetProperty("maxMessagesBeforeCompact", out var m) && m.TryGetInt32(out var n) ? n : 30,
            ReserveRecent = el.TryGetProperty("reserveRecent", out var r) && r.TryGetInt32(out var rr) ? rr : 10
        };
    }

    private static BrowserConfig ParseBrowser(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return new BrowserConfig();
        return new BrowserConfig
        {
            Headless = el.TryGetProperty("headless", out var h) ? h.GetBoolean() : false,
            Channel = el.TryGetProperty("channel", out var c) ? c.GetString()?.Trim() : null
        };
    }

    private static List<string> ParseStringArray(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            var s = item.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                list.Add(s);
        }
        return list;
    }
}

/// <summary>Application configuration.</summary>
public sealed class AppConfig
{
    public ToolPolicyConfig ToolPolicy { get; init; } = new();
    public ModelConfig Model { get; init; } = new();
    public CompactionConfig Compaction { get; init; } = new();
    public BrowserConfig Browser { get; init; } = new();
    /// <summary>Workspace root path. Default: AppContext.BaseDirectory/workspace.</summary>
    public string? Workspace { get; init; }
    /// <summary>IANA timezone for timestamp injection (e.g. Asia/Shanghai). Default: local.</summary>
    public string? UserTimezone { get; init; }
}

/// <summary>Model configuration.</summary>
public sealed class ModelConfig
{
    public string Provider { get; init; } = "openai";
    public string Model { get; init; } = "gpt-4o-mini";
    public string? BaseUrl { get; init; }
    /// <summary>API key from openlum.json (no env var).</summary>
    public string? ApiKey { get; init; }
}

/// <summary>Compaction configuration.</summary>
public sealed class CompactionConfig
{
    public bool Enabled { get; init; }
    public int MaxMessagesBeforeCompact { get; init; } = 30;
    public int ReserveRecent { get; init; } = 10;
}

/// <summary>Browser configuration.</summary>
public sealed class BrowserConfig
{
    /// <summary>When false, browser window stays visible so you can see operations.</summary>
    public bool Headless { get; init; } = false;
    /// <summary>Browser channel (e.g. "msedge" for Edge). Default: msedge on Windows.</summary>
    public string? Channel { get; init; }
}
