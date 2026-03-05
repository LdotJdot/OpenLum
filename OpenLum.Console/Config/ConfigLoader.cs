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
                    Agent = ParseAgent(root.TryGetProperty("agent", out var ag) ? ag : default),
                    Browser = ParseBrowser(root.TryGetProperty("browser", out var b) ? b : default),
                    Workspace = root.TryGetProperty("workspace", out var w) ? w.GetString()?.Trim() : null,
                    UserTimezone = root.TryGetProperty("userTimezone", out var tz) ? tz.GetString()?.Trim() : null
                };
            }
            catch (Exception ex)
            {
                var message = $"[Config] Failed to load '{path}': {ex.GetType().Name}: {ex.Message}";
                try
                {
                    System.Console.Error.WriteLine(message);
                }
                catch
                {
                    // ignore console failures (non-console hosts)
                }

                if (IsStrictConfig())
                {
                    throw new InvalidOperationException(message, ex);
                }
            }
        }

        return new AppConfig();
    }

    private static bool IsStrictConfig()
    {
        var value = Environment.GetEnvironmentVariable("OPENLUM_STRICT_CONFIG");
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
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
        var config = new ModelConfig();
        if (el.ValueKind == JsonValueKind.Object)
        {
            config = new ModelConfig
            {
                Provider = el.TryGetProperty("provider", out var p) ? p.GetString() ?? "openai" : "openai",
                Model = el.TryGetProperty("model", out var m) ? m.GetString() ?? "gpt-4o-mini" : "gpt-4o-mini",
                BaseUrl = el.TryGetProperty("baseUrl", out var u) ? u.GetString() : null,
                ApiKey = el.TryGetProperty("apiKey", out var k) ? k.GetString() : null,
                NoThinking = el.TryGetProperty("noThinking", out var nt) ? nt.GetBoolean() : false
            };
        }

        // Environment variables override JSON config where present.
        var providerEnv = Environment.GetEnvironmentVariable("OPENLUM_PROVIDER");
        var modelEnv = Environment.GetEnvironmentVariable("OPENLUM_MODEL");
        var baseUrlEnv = Environment.GetEnvironmentVariable("OPENLUM_BASE_URL");
        var apiKeyEnv = Environment.GetEnvironmentVariable("OPENLUM_API_KEY");

        return new ModelConfig
        {
            Provider = !string.IsNullOrWhiteSpace(providerEnv) ? providerEnv : config.Provider,
            Model = !string.IsNullOrWhiteSpace(modelEnv) ? modelEnv : config.Model,
            BaseUrl = !string.IsNullOrWhiteSpace(baseUrlEnv) ? baseUrlEnv : config.BaseUrl,
            ApiKey = !string.IsNullOrWhiteSpace(apiKeyEnv) ? apiKeyEnv : config.ApiKey,
            NoThinking = config.NoThinking
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
            ReserveRecent = el.TryGetProperty("reserveRecent", out var r) && r.TryGetInt32(out var rr) ? rr : 10,
            MaxToolResultChars = el.TryGetProperty("maxToolResultChars", out var tr) && tr.TryGetInt32(out var trn) ? trn : 8000
        };
    }

    private static AgentConfig ParseAgent(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return new AgentConfig();

        var strategy = el.TryGetProperty("turnLimitStrategy", out var s) ? s.GetString()?.Trim() : null;
        return new AgentConfig
        {
            MaxToolTurns = el.TryGetProperty("maxToolTurns", out var m) && m.TryGetInt32(out var n) ? n : 100,
            TurnLimitStrategy = strategy is "force_stop" or "model_decide" ? strategy : "model_decide"
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
    public AgentConfig Agent { get; init; } = new();
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
    /// <summary>Ollama: disable thinking mode (send think: false). Default: false = thinking enabled.</summary>
    public bool NoThinking { get; init; } = false;
}

/// <summary>Compaction configuration.</summary>
public sealed class CompactionConfig
{
    public bool Enabled { get; init; }
    public int MaxMessagesBeforeCompact { get; init; } = 30;
    public int ReserveRecent { get; init; } = 10;
    /// <summary>Maximum characters from a single tool result to keep in session history.</summary>
    public int MaxToolResultChars { get; init; } = 8000;
}

/// <summary>Agent loop configuration (tool turn limit and behavior at limit).</summary>
public sealed class AgentConfig
{
    /// <summary>Max tool-call rounds per user message. Default 100.</summary>
    public int MaxToolTurns { get; init; } = 100;
    /// <summary>When limit reached: "model_decide" = ask model to choose stop/continue/ask_user; "force_stop" = force summary.</summary>
    public string TurnLimitStrategy { get; init; } = "model_decide";
}

/// <summary>Browser configuration.</summary>
public sealed class BrowserConfig
{
    /// <summary>When false, browser window stays visible so you can see operations.</summary>
    public bool Headless { get; init; } = false;
    /// <summary>Browser channel (e.g. "msedge" for Edge). Default: msedge on Windows.</summary>
    public string? Channel { get; init; }
}
