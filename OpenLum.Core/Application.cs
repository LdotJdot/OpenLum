using System.Text;
using OpenLum.Console.Agent;
using OpenLum.Console.Compaction;
using OpenLum.Console.Config;
using OpenLum.Console.Console;
using OpenLum.Console.Hosting;
using OpenLum.Console.Interfaces;
using OpenLum.Console.IO;
using OpenLum.Console.Observability;
using OpenLum.Console.Providers;
using OpenLum.Console.Session;
using OpenLum.Console.Tools;

namespace OpenLum.Console;

/// <summary>Main application: loads config, wires up agent and session, runs the REPL.</summary>
public sealed class Application
{
    private static readonly object ConsoleLock = new();

    /// <summary>Set for each main REPL <see cref="RunAsync"/> so <see cref="AgentLoop"/> can flush streamed text before tool lines.</summary>
    private static ThinkAwareConsoleProgress? s_currentReplAssistantProgress;


    /// <summary>
    /// Backward compatibility shim. Host should initialize console encoding before calling <see cref="Run"/>.
    /// </summary>
    public static void EnsureConsoleUtf8Initialized()
    {
        // Intentionally no-op in Core to preserve dependency direction:
        // Core depends on input abstractions only; host owns concrete console initialization.
    }

    public static int Run(string[]? args, IReplLineInput replLineInput)
    {
        var argv = args ?? Array.Empty<string>();
        var lineInput = replLineInput ?? throw new ArgumentNullException(nameof(replLineInput));
        if (argv.Length > 0 && argv.Contains("--config-doctor", StringComparer.OrdinalIgnoreCase))
        {
            return RunConfigDoctor();
        }

        string? replFilePath = null;
        var replFileEntire = false;
        string? continuePath = null;
        string? conversationFileCli = null;
        for (var i = 0; i < argv.Length; i++)
        {
            if (string.Equals(argv[i], "--repl-file-entire", StringComparison.OrdinalIgnoreCase))
            {
                replFileEntire = true;
                continue;
            }

            if (string.Equals(argv[i], "--repl-file", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
            {
                replFilePath = argv[++i].Trim();
                continue;
            }

            if (string.Equals(argv[i], "--continue", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
            {
                continuePath = argv[++i].Trim();
                continue;
            }

            if (string.Equals(argv[i], "--conversation-file", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
            {
                conversationFileCli = argv[++i].Trim();
                continue;
            }
        }

        if (string.IsNullOrEmpty(continuePath) && argv.Length == 1 &&
            argv[0].EndsWith(".openlum", StringComparison.OrdinalIgnoreCase))
        {
            var sh = Path.GetFullPath(argv[0]);
            if (File.Exists(sh))
                continuePath = argv[0];
        }

        if (replFileEntire && string.IsNullOrEmpty(replFilePath))
        {
            System.Console.Error.WriteLine("[OpenLum] --repl-file-entire requires --repl-file <path>.");
            return 1;
        }

        var config = ConfigLoader.Load();
        var workspaceDir = ResolveWorkspace(config.Workspace);
        ConversationLoadResult? resumed = null;
        if (!string.IsNullOrEmpty(continuePath))
        {
            try
            {
                resumed = ConversationRecordSerializer.Load(Path.GetFullPath(continuePath));
                if (!string.IsNullOrWhiteSpace(resumed.Workspace))
                    workspaceDir = ResolveWorkspace(resumed.Workspace);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"[OpenLum] --continue failed: {ex.Message}");
                return 1;
            }
        }

        Directory.CreateDirectory(workspaceDir);

        var sessionLog = SessionRunLogger.TryCreate(workspaceDir, $"{config.Model.Provider}/{config.Model.Model}", out var sessionLogPath);
        if (!string.IsNullOrEmpty(sessionLogPath))
        {
            System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
            System.Console.WriteLine($"Session log: {sessionLogPath}");
            System.Console.ResetColor();
        }

        var apiKey = config.Model.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
            apiKey = Environment.GetEnvironmentVariable("OPENLUM_API_KEY", EnvironmentVariableTarget.User)?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            System.Console.WriteLine("Set apiKey in openlum.json (or openlum.console.json), or set OPENLUM_API_KEY environment variable.");
            return 1;
        }

        var resolver = new WorkspacePathResolver(workspaceDir, SkillLoader.GetSkillRoots(workspaceDir));

        var todoStore = new TodoStore();
        var planStore = new PlanStore();
        var session = new ConsoleSession();
        if (resumed is not null)
        {
            session.ReplaceAllMessages(resumed.Messages);
            if (resumed.Todos.Count > 0)
                todoStore.Replace([.. resumed.Todos]);
            else
                todoStore.Clear();
            planStore.Set(resumed.Plan);

            var ml = $"{config.Model.Provider}/{config.Model.Model}";
            if (!string.IsNullOrEmpty(resumed.ModelLabel) &&
                !string.Equals(resumed.ModelLabel, ml, StringComparison.OrdinalIgnoreCase))
            {
                System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                System.Console.WriteLine(
                    $"[OpenLum] Resuming with current model ({ml}); record was saved as: {resumed.ModelLabel}");
                System.Console.ResetColor();
            }

            System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
            System.Console.WriteLine($"[OpenLum] Resumed conversation ({session.MessageCount} messages).");
            System.Console.ResetColor();
        }

        var conversationOutPath = ResolveConversationOutPath(workspaceDir, config, conversationFileCli);
        if (!string.IsNullOrEmpty(conversationOutPath))
        {
            System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
            System.Console.WriteLine($"Conversation record: {conversationOutPath}");
            System.Console.ResetColor();
        }

        void OnPlanCommittedEcho(string planText)
        {
            if (string.IsNullOrWhiteSpace(planText))
                return;
            lock (ConsoleLock)
            {
                System.Console.WriteLine();
                var prev = System.Console.ForegroundColor;
                System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                System.Console.WriteLine(planText);
                System.Console.ForegroundColor = prev;
            }
        }

        var baseTools = new ToolRegistry();
        baseTools.Register(new ReadTool(resolver));
        baseTools.Register(new ReadManyTool(resolver));
        baseTools.Register(new WriteTool(resolver));
        baseTools.Register(new StrReplaceTool(resolver));
        baseTools.Register(new TextEditTool(resolver));
        baseTools.Register(new ListDirTool(resolver));
        baseTools.Register(new GrepTool(resolver, config.Search));
        baseTools.Register(new GlobTool(resolver, config.Search));
        baseTools.Register(new TodoTool(todoStore));
        baseTools.Register(new SubmitPlanTool(planStore, OnPlanCommittedEcho));
        baseTools.Register(new ExecTool(workspaceDir));
        baseTools.Register(new MemoryGetTool(workspaceDir));
        baseTools.Register(new MemorySearchTool(workspaceDir));

        var think = config.Model.NoThinking ? false : (bool?)null;
        var model = new OpenAIModelProvider(
            apiKey,
            model: config.Model.Model,
            baseUrl: config.Model.BaseUrl,
            think: think
        );

        IToolRegistry toolsFiltered = new ToolPolicyFilter(baseTools, config.ToolPolicy);
        var subagentPrompt = ComposeSystemPrompt(workspaceDir, toolsFiltered, config.PromptOverlay);
        SessionCompactor? compactor = null;
        if (config.Compaction.Enabled)
        {
            compactor = new SessionCompactor(
                model,
                maxMessagesBeforeCompact: config.Compaction.MaxMessagesBeforeCompact,
                reserveRecent: config.Compaction.ReserveRecent,
                collapseFailedAttempts: config.Compaction.CollapseFailedAttempts,
                onCompactionSummary: summary => sessionLog?.WriteCompaction(summary));
        }
        baseTools.Register(
            new SessionsSpawnTool(
                model,
                toolsFiltered,
                workspaceDir,
                subagentPrompt,
                compactor: compactor,
                maxToolTurns: config.Agent.MaxToolTurns,
                maxToolResultChars: config.Compaction.MaxToolResultChars > 0 ? config.Compaction.MaxToolResultChars : null,
                maxFailedToolResultChars: config.Compaction.MaxFailedToolResultChars > 0 ? config.Compaction.MaxFailedToolResultChars : null,
                useModelDecideAtLimit: config.Agent.TurnLimitStrategy == "model_decide",
                sessionRunLogger: sessionLog,
                workflow: config.Workflow,
                onPlanCommitted: OnPlanCommittedEcho)
        );

        IToolRegistry tools = new ToolPolicyFilter(baseTools, config.ToolPolicy);
        var systemPrompt = ComposeSystemPrompt(workspaceDir, tools, config.PromptOverlay);

        var modelLabel = $"{config.Model.Provider}/{config.Model.Model}";
        void PersistConversation()
        {
            if (string.IsNullOrEmpty(conversationOutPath))
                return;
            try
            {
                ConversationRecordSerializer.Save(
                    conversationOutPath,
                    workspaceDir,
                    modelLabel,
                    session.Messages,
                    todoStore.GetSnapshot(),
                    planStore.GetCurrentPlanText());
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"[OpenLum] conversation save failed: {ex.Message}");
            }
        }

        var agent = new AgentLoop(
            model,
            tools,
            session,
            systemPrompt,
            compactor: compactor,
            todoStore: todoStore,
            planStore: planStore,
            workflow: config.Workflow,
            onToolStarting: (name, args) =>
            {
                lock (ConsoleLock)
                {
                    var prev = System.Console.ForegroundColor;
                    System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                    System.Console.WriteLine();
                    System.Console.WriteLine($"  {ToolActivityFormatter.FormatStarting(name, args)}");
                    System.Console.ForegroundColor = prev;
                }
            },
            onToolCompleted: (name, args, success) =>
            {
                lock (ConsoleLock)
                {
                    var prev = System.Console.ForegroundColor;
                    System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                    System.Console.WriteLine($"  {ToolActivityFormatter.FormatCompleted(name, args, success)}");
                    System.Console.ForegroundColor = prev;
                }
            },
            flushAssistantStreamBeforeTools: () =>
            {
                lock (ConsoleLock)
                {
                    s_currentReplAssistantProgress?.FlushRemaining();
                    System.Console.WriteLine();
                }
            },
            maxToolResultChars: config.Compaction.MaxToolResultChars,
            maxFailedToolResultChars: config.Compaction.MaxFailedToolResultChars > 0 ? config.Compaction.MaxFailedToolResultChars : null,
            maxToolTurns: config.Agent.MaxToolTurns,
            useModelDecideAtLimit: config.Agent.TurnLimitStrategy == "model_decide",
            runLogger: sessionLog
        );

        System.Console.WriteLine(
            "OpenLum Console — generic agent shell. Use /help for commands (-restart for a fresh session). " +
            "Flags: --continue <file.openlum> | path.openlum · --conversation-file <path> · --repl-file / --repl-file-entire.");
        System.Console.WriteLine();

        if (!string.IsNullOrEmpty(replFilePath))
        {
            var abs = Path.GetFullPath(replFilePath);
            if (!File.Exists(abs))
            {
                System.Console.Error.WriteLine($"[OpenLum] --repl-file not found: {abs}");
                sessionLog?.Dispose();
                return 1;
            }

            try
            {
                if (replFileEntire)
                {
                    var entire = File.ReadAllText(abs, Encoding.UTF8).Trim();
                    if (string.IsNullOrEmpty(entire))
                    {
                        System.Console.WriteLine("[OpenLum] --repl-file-entire: file is empty; exiting.");
                        return 0;
                    }

                    System.Console.WriteLine($"[OpenLum] Single user message from entire file: {abs}");
                    System.Console.WriteLine();
                    RunReplUserQuery(agent, workspaceDir, config.UserTimezone, entire, lineInput,
                        PersistConversation);
                    return 0;
                }

                using var replReader = new StreamReader(abs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    detectEncodingFromByteOrderMarks: true);
                var fileLineInput = ReplLineInput.From(replReader.ReadLine);
                return RunRepl(agent, session, workspaceDir, fileLineInput, config.UserTimezone, todoStore, planStore, sessionLog,
                    replFromFile: true, persistConversation: PersistConversation);
            }
            finally
            {
                sessionLog?.Dispose();
            }
        }

        try
        {
            return RunRepl(agent, session, workspaceDir, lineInput, config.UserTimezone, todoStore, planStore, sessionLog,
                replFromFile: false, persistConversation: PersistConversation);
        }
        finally
        {
            sessionLog?.Dispose();
        }
    }

    /// <summary>One chat turn: timestamp injection, model run, optional confirm (uses <paramref name="readReplForConfirm"/>).</summary>
    private static void RunReplUserQuery(
        AgentLoop agent,
        string workspaceDir,
        string? userTimezone,
        string input,
        IReplLineInput readReplForConfirm,
        Action? persistConversation = null)
    {
        var stampedInput = TimestampInjection.Inject(input.Trim(), userTimezone);

        var task = new Models.ConsoleTask(
            TaskType: "GenericChat",
            TargetScope: workspaceDir,
            FocusAreas: Array.Empty<string>(),
            UserQuery: stampedInput);

        lock (ConsoleLock)
        {
            System.Console.WriteLine();
            System.Console.ForegroundColor = System.ConsoleColor.Green;
            System.Console.Write("Assistant: ");
            System.Console.ResetColor();
        }

        var progress = new ThinkAwareConsoleProgress(ConsoleLock);
        s_currentReplAssistantProgress = progress;
        AgentTurnResult result;
        try
        {
            result = agent.RunAsync(task.UserQuery, progress, CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            s_currentReplAssistantProgress = null;
        }

        progress.FlushRemaining();
        System.Console.WriteLine();
        if (result.NeedsUserConfirmation)
        {
            var msg = result.ConfirmMessage ?? "是否继续？";
            lock (ConsoleLock)
            {
                System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                System.Console.WriteLine(msg);
                System.Console.Write("继续？(y/n) ");
                System.Console.ResetColor();
            }

            var confirm = readReplForConfirm.ReadLine()?.Trim().ToLowerInvariant();
            if (confirm is "y" or "yes" or "继续" or "是")
            {
                lock (ConsoleLock)
                {
                    System.Console.WriteLine();
                    System.Console.ForegroundColor = System.ConsoleColor.Green;
                    System.Console.Write("Assistant: ");
                    System.Console.ResetColor();
                }

                var progress2 = new ThinkAwareConsoleProgress(ConsoleLock);
                s_currentReplAssistantProgress = progress2;
                try
                {
                    agent.RunAsync("用户确认继续", progress2, CancellationToken.None).GetAwaiter().GetResult();
                }
                finally
                {
                    s_currentReplAssistantProgress = null;
                }

                progress2.FlushRemaining();
                System.Console.WriteLine();
            }
        }
        else if (!result.Success && result.ErrorMessage is { } err)
        {
            System.Console.ForegroundColor = System.ConsoleColor.Red;
            System.Console.WriteLine($"Error: {err}");
            System.Console.ResetColor();
        }

        persistConversation?.Invoke();
    }

    private static int RunConfigDoctor()
    {
        System.Console.WriteLine("== OpenLum Config Doctor ==");
        var config = ConfigLoader.Load();
        System.Console.WriteLine($"Profile   : {config.ToolPolicy.Profile}");
        System.Console.WriteLine($"Workspace : {ResolveWorkspace(config.Workspace)}");
        System.Console.WriteLine($"Model     : provider={config.Model.Provider}, model={config.Model.Model}, baseUrl={config.Model.BaseUrl ?? "(default)"}");

        var skills = SkillLoader.Load(ResolveWorkspace(config.Workspace ?? "."));
        System.Console.WriteLine();
        System.Console.WriteLine($"Skills ({skills.Count}):");
        foreach (var s in skills)
        {
            System.Console.WriteLine($"  - {s.Name} @ {s.Location}");
        }

        return 0;
    }

    private static int RunRepl(
        AgentLoop agent,
        ConsoleSession session,
        string workspaceDir,
        IReplLineInput readRepl,
        string? userTimezone = null,
        TodoStore? todoStore = null,
        PlanStore? planStore = null,
        SessionRunLogger? sessionLog = null,
        bool replFromFile = false,
        Action? persistConversation = null)
    {
        while (true)
        {
            lock (ConsoleLock)
            {
                System.Console.ForegroundColor = System.ConsoleColor.Blue;
                System.Console.Write("You: > ");
                System.Console.ResetColor();
            }

            var raw = readRepl.ReadLine();
            if (raw is null && replFromFile)
                return 0;
            var input = raw?.Trim() ?? "";
            if (string.IsNullOrEmpty(input))
                continue;

            if (string.Equals(input, "-restart", StringComparison.OrdinalIgnoreCase))
            {
                PromptAndRestartSession(session, todoStore, planStore, sessionLog, readRepl);
                continue;
            }

            if (input.StartsWith('/'))
            {
                var cmd = input[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[
                    0
                ].ToLowerInvariant();
                switch (cmd)
                {
                    case "quit":
                    case "exit":
                        return 0;
                    case "clear":
                        session.Clear();
                        todoStore?.Clear();
                        planStore?.Clear();
                        System.Console.WriteLine("Session cleared.");
                        continue;
                    case "restart":
                        PromptAndRestartSession(session, todoStore, planStore, sessionLog, readRepl);
                        continue;
                    case "help":
                        System.Console.WriteLine("  /help     — show this");
                        System.Console.WriteLine("  /clear    — clear session history (no confirmation)");
                        System.Console.WriteLine("  /restart  — same as -restart");
                        System.Console.WriteLine("  -restart  — discard all context and start fresh (confirm; no model)");
                        System.Console.WriteLine("  /quit     — exit");
                        System.Console.WriteLine("  --repl-file <path> — (launch flag) UTF-8 line script instead of typing");
                        System.Console.WriteLine("  --repl-file-entire — (launch flag) with --repl-file: whole file = one user message");
                        System.Console.WriteLine("  --continue <file.openlum> — resume chat history (or path.openlum alone)");
                        System.Console.WriteLine("  --conversation-file <path> — auto-save record after each turn (overrides config)");
                        continue;
                }
            }

            RunReplUserQuery(agent, workspaceDir, userTimezone, input, readRepl, persistConversation);
        }
    }

    /// <summary>
    /// Host-only: clears chat history and auxiliary state after user confirms. Does not call the model.
    /// </summary>
    private static void PromptAndRestartSession(
        ConsoleSession session,
        TodoStore? todoStore,
        PlanStore? planStore,
        SessionRunLogger? sessionLog,
        IReplLineInput readRepl)
    {
        lock (ConsoleLock)
        {
            System.Console.ForegroundColor = System.ConsoleColor.Yellow;
            System.Console.WriteLine(
                "Discard all chat history, TODOs, and plans, and start a new session? (no API call) [y/n]");
            System.Console.ResetColor();
        }

        var confirm = readRepl.ReadLine()?.Trim().ToLowerInvariant();
        if (confirm is not ("y" or "yes" or "是"))
        {
            lock (ConsoleLock)
            {
                System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                System.Console.WriteLine("Restart cancelled.");
                System.Console.ResetColor();
            }
            return;
        }

        session.Clear();
        todoStore?.Clear();
        planStore?.Clear();
        sessionLog?.WriteLogicalSessionBoundary("user -restart / /restart");
        lock (ConsoleLock)
        {
            System.Console.ForegroundColor = System.ConsoleColor.Green;
            System.Console.WriteLine("Session restarted — context cleared.");
            System.Console.ResetColor();
        }
    }

    /// <summary>Built-in system prompt plus optional <see cref="AppConfig.PromptOverlay"/> (product/deployment hints).</summary>
    private static string ComposeSystemPrompt(string workspaceDir, IToolRegistry tools, string? promptOverlay)
    {
        var s = SystemPromptBuilder.Build(workspaceDir, tools);
        var o = promptOverlay?.Trim();
        if (string.IsNullOrEmpty(o))
            return s;
        return s + "\n\n## Config overlay\n" + o;
    }

    private static string ResolveWorkspace(string? configWorkspace)
    {
        if (!string.IsNullOrWhiteSpace(configWorkspace))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configWorkspace);
            return Path.GetFullPath(expanded);
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "workspace"));
    }

    /// <summary>CLI &gt; OPENLUM_CONVERSATION_FILE &gt; config.conversation.</summary>
    private static string? ResolveConversationOutPath(string workspaceDir, AppConfig config, string? cliPath)
    {
        if (!string.IsNullOrWhiteSpace(cliPath))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(cliPath.Trim()));

        var env = Environment.GetEnvironmentVariable("OPENLUM_CONVERSATION_FILE")?.Trim();
        if (!string.IsNullOrEmpty(env))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(env));

        if (!config.Conversation.AutoSave)
            return null;

        var p = config.Conversation.Path?.Trim();
        if (string.IsNullOrEmpty(p))
            p = ".openlum/conversations/latest.openlum";

        return Path.IsPathRooted(p)
            ? Path.GetFullPath(Environment.ExpandEnvironmentVariables(p))
            : Path.GetFullPath(Path.Combine(workspaceDir, p));
    }

    /// <summary>IProgress that invokes synchronously (avoids late callbacks that interleave with prompt).</summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SyncProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }
}
