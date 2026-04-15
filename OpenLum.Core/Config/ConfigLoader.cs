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
                    Workflow = ParseWorkflow(root.TryGetProperty("workflow", out var wf) ? wf : default),
                    Search = ParseSearch(root.TryGetProperty("search", out var se) ? se : default),
                    Workspace = root.TryGetProperty("workspace", out var w) ? w.GetString()?.Trim() : null,
                    HostRoot = root.TryGetProperty("hostRoot", out var hr) ? hr.GetString()?.Trim() : null,
                    UserTimezone = root.TryGetProperty("userTimezone", out var tz) ? tz.GetString()?.Trim() : null,
                    Conversation = ParseConversation(root.TryGetProperty("conversation", out var cv) ? cv : default),
                    PromptOverlay = root.TryGetProperty("promptOverlay", out var po) ? po.GetString()?.Trim() : null
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

        // If apiKey in JSON is empty, use OPENLUM_API_KEY env var; otherwise env overrides JSON when set.
        var resolvedApiKey = string.IsNullOrWhiteSpace(config.ApiKey)
            ? apiKeyEnv
            : (!string.IsNullOrWhiteSpace(apiKeyEnv) ? apiKeyEnv : config.ApiKey);

        return new ModelConfig
        {
            Provider = !string.IsNullOrWhiteSpace(providerEnv) ? providerEnv : config.Provider,
            Model = !string.IsNullOrWhiteSpace(modelEnv) ? modelEnv : config.Model,
            BaseUrl = !string.IsNullOrWhiteSpace(baseUrlEnv) ? baseUrlEnv : config.BaseUrl,
            ApiKey = resolvedApiKey,
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
            MaxToolResultChars = el.TryGetProperty("maxToolResultChars", out var tr) && tr.TryGetInt32(out var trn) ? trn : 8000,
            MaxFailedToolResultChars = el.TryGetProperty("maxFailedToolResultChars", out var tf) && tf.TryGetInt32(out var tfn) ? tfn : 400,
            CollapseFailedAttempts = el.TryGetProperty("collapseFailedAttempts", out var cf) ? cf.GetBoolean() : true
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

    private static WorkflowConfig ParseWorkflow(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return new WorkflowConfig();

        return new WorkflowConfig
        {
            // Default on when key omitted; set "enabled": false to disable.
            Enabled = !el.TryGetProperty("enabled", out var e) || e.GetBoolean(),
            RequirePlanForWrite = el.TryGetProperty("requirePlanForWrite", out var rp) ? rp.GetBoolean() : true,
            AutoVerifyAfterFirstWrite = el.TryGetProperty("autoVerifyAfterFirstWrite", out var av) && av.GetBoolean(),
            // Default true when key omitted: exec allowed in Observe without prior plan (see AgentLoop).
            AllowExecInObserve = !el.TryGetProperty("allowExecInObserve", out var ax) || ax.GetBoolean(),
            InstructionThinking = el.TryGetProperty("instructionThinking", out var ins) ? (ins.GetString()?.Trim() ?? "auto") : "auto",
            InstructionThinkingMinChars = el.TryGetProperty("instructionThinkingMinChars", out var im) && im.TryGetInt32(out var imc)
                ? Math.Clamp(imc, 50, 10_000)
                : 320
        };
    }

    private static ConversationConfig ParseConversation(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return new ConversationConfig();

        return new ConversationConfig
        {
            AutoSave = el.TryGetProperty("autoSave", out var a) && a.GetBoolean(),
            Path = el.TryGetProperty("path", out var p) ? p.GetString()?.Trim() : null
        };
    }

    private static SearchConfig ParseSearch(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return new SearchConfig();

        return new SearchConfig
        {
            SkipDirs = el.TryGetProperty("skipDirs", out var sd) ? ParseStringArray(sd) : [],
            SkipGlobs = el.TryGetProperty("skipGlobs", out var sg) ? ParseStringArray(sg) : []
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
    public WorkflowConfig Workflow { get; init; } = new();
    public SearchConfig Search { get; init; } = new();
    /// <summary>Workspace root path. Default: AppContext.BaseDirectory/workspace.</summary>
    public string? Workspace { get; init; }

    /// <summary>
    /// Host bundle directory containing <c>skills/</c> (or <c>Skills/</c>) and <c>InternalTools/</c>.
    /// Default when unset: <see cref="AppContext.BaseDirectory"/> (exe directory). Override with env <c>OPENLUM_HOST_ROOT</c>.
    /// </summary>
    public string? HostRoot { get; init; }
    /// <summary>IANA timezone ID for timestamp injection. Default: local.</summary>
    public string? UserTimezone { get; init; }

    /// <summary>Optional automatic .openlum conversation file.</summary>
    public ConversationConfig Conversation { get; init; } = new();

    /// <summary>
    /// Optional extra system text appended after <see cref="SystemPromptBuilder"/> output under <c>## Config overlay</c>.
    /// Use for deployment- or team-specific constraints without editing code. Keep tool-specific how-to in tool descriptions.
    /// </summary>
    public string? PromptOverlay { get; init; }
}

/// <summary>Automatic conversation record under workspace.</summary>
public sealed class ConversationConfig
{
    /// <summary>When true, persist after each completed user turn if <see cref="Path"/> or default path is set.</summary>
    public bool AutoSave { get; init; }

    /// <summary>Relative to workspace or absolute. Default when empty: .openlum/conversations/latest.openlum</summary>
    public string? Path { get; init; }
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
    /// <summary>When sending to model: cap length of failed tool results to save tokens. 0 = no cap. Default 400.</summary>
    public int MaxFailedToolResultChars { get; init; } = 400;
    /// <summary>When compacting: merge consecutive failed tool attempts into one short note before summarizing. Default true.</summary>
    public bool CollapseFailedAttempts { get; init; } = true;
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
    /// <summary>Browser channel name. Default: msedge on Windows.</summary>
    public string? Channel { get; init; }
}

/// <summary>
/// Optional workflow gating (phases) to improve safety and reduce premature writes.
/// </summary>
public sealed class WorkflowConfig
{
    /// <summary>When true, tools are exposed in phases (Observe -> Act -> Verify). Default true (built-in).</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>
    /// When true, write-like tools (write/str_replace/text_edit) are gated behind submit_plan or TODOs in Observe.
    /// Does not gate exec when <see cref="AllowExecInObserve"/> is true.
    /// </summary>
    public bool RequirePlanForWrite { get; init; } = true;
    /// <summary>
    /// When true (default), exec is allowed in Observe without a prior plan (fewer rounds for commands/tests).
    /// When false, exec is only available in Act/Verify (legacy).
    /// </summary>
    public bool AllowExecInObserve { get; init; } = true;
    /// <summary>If true, auto-switch to Verify after first write-like tool executes.</summary>
    public bool AutoVerifyAfterFirstWrite { get; init; } = false;
    /// <summary>
    /// When workflow is enabled: <c>never</c> | <c>auto</c> | <c>always</c> — insert a no-tool round right after the user message
    /// to output &lt;thinking&gt; before Observe/Act. <c>auto</c> uses heuristics (length, bullets, ambiguity keywords).
    /// </summary>
    public string InstructionThinking { get; init; } = "auto";
    /// <summary>When <see cref="InstructionThinking"/> is auto: prompt length ≥ this suggests a long-form / complex request.</summary>
    public int InstructionThinkingMinChars { get; init; } = 320;
}

/// <summary>
/// Search/scan configuration for grep/glob tools.
/// </summary>
public sealed class SearchConfig
{
    /// <summary>Directory names to skip when scanning (path segments).</summary>
    public IReadOnlyList<string> SkipDirs { get; init; } = [];
    /// <summary>Path/file globs to skip when scanning (matched against relative path).</summary>
    public IReadOnlyList<string> SkipGlobs { get; init; } = [];
}
