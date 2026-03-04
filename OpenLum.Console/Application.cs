using OpenLum.Console.Agent;
using OpenLum.Console.Compaction;
using OpenLum.Console.Config;
using OpenLum.Console.Interfaces;
using OpenLum.Console.Providers;
using OpenLum.Console.Session;
using OpenLum.Console.Tools;

namespace OpenLum.Console;

/// <summary>Main application: loads config, wires up agent and session, runs the REPL.</summary>
public sealed class Application
{
    private static readonly object ConsoleLock = new();

    public static int Run()
    {
        var config = ConfigLoader.Load();
        var workspaceDir = ResolveWorkspace(config.Workspace);
        Directory.CreateDirectory(workspaceDir);

        var apiKey = config.Model.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            System.Console.WriteLine("Set apiKey in openlum.json (or openlum.console.json).");
            return 1;
        }

        var baseTools = new ToolRegistry();
        baseTools.Register(new ReadTool(workspaceDir, SkillLoader.GetSkillRoots(workspaceDir)));
        baseTools.Register(new WriteTool(workspaceDir));
        baseTools.Register(new ListDirTool(workspaceDir));
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
        var subagentPrompt = SystemPromptBuilder.Build(workspaceDir, toolsFiltered);
        baseTools.Register(
            new SessionsSpawnTool(model, toolsFiltered, workspaceDir, subagentPrompt)
        );

        IToolRegistry tools = new ToolPolicyFilter(baseTools, config.ToolPolicy);
        var systemPrompt = SystemPromptBuilder.Build(workspaceDir, tools);

        var session = new ConsoleSession();

        SessionCompactor? compactor = null;
        if (config.Compaction.Enabled)
        {
            compactor = new SessionCompactor(
                model,
                maxMessagesBeforeCompact: config.Compaction.MaxMessagesBeforeCompact,
                reserveRecent: config.Compaction.ReserveRecent
            );
        }

        var agent = new AgentLoop(
            model,
            tools,
            session,
            systemPrompt,
            compactor,
            onToolExecuting: (name, args) =>
            {
                lock (ConsoleLock)
                {
                    var prev = System.Console.ForegroundColor;
                    System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
                    System.Console.WriteLine($"  → {name}({args})");
                    System.Console.ForegroundColor = prev;
                }
            },
            maxToolResultChars: config.Compaction.MaxToolResultChars,
            maxToolTurns: config.Agent.MaxToolTurns,
            useModelDecideAtLimit: config.Agent.TurnLimitStrategy == "model_decide"
        );

        System.Console.WriteLine("OpenLum Console — generic agent shell. Use /help for commands.");
        System.Console.WriteLine();

        return RunRepl(agent, session, workspaceDir, config.UserTimezone);
    }

    private static int RunRepl(
        AgentLoop agent,
        ConsoleSession session,
        string workspaceDir,
        string? userTimezone = null)
    {
        while (true)
        {
            lock (ConsoleLock)
            {
                System.Console.ForegroundColor = System.ConsoleColor.Blue;
                System.Console.Write("You: > ");
                System.Console.ResetColor();
            }
            var input = System.Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
                continue;

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
                        System.Console.WriteLine("Session cleared.");
                        continue;
                    case "help":
                        System.Console.WriteLine("  /help  — show this");
                        System.Console.WriteLine("  /clear — clear session history");
                        System.Console.WriteLine("  /quit  — exit");
                        continue;
                }
            }

            // Inject current date/time so the model knows "today" for news, schedules, etc.
            var stampedInput = TimestampInjection.Inject(input, userTimezone);

            var task = new Models.ConsoleTask(
                TaskType: "GenericChat",
                TargetScope: workspaceDir,
                FocusAreas: Array.Empty<string>(),
                UserQuery: stampedInput);

            // Use synchronous progress: Progress<T>.Report() posts to thread pool and can run
            // after RunAsync returns, causing "> " and trailing text to interleave.
            lock (ConsoleLock)
            {
                System.Console.WriteLine();
                System.Console.ForegroundColor = System.ConsoleColor.Green;
                System.Console.Write("Assistant: ");
                System.Console.ResetColor();
            }
            var progress = new SyncProgress<string>(c =>
            {
                lock (ConsoleLock)
                {
                    System.Console.Write(c);
                }
            });
            var result = agent
                .RunAsync(task.UserQuery, progress, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
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
                var confirm = System.Console.ReadLine()?.Trim().ToLowerInvariant();
                if (confirm is "y" or "yes" or "继续" or "是")
                {
                    lock (ConsoleLock)
                    {
                        System.Console.WriteLine();
                        System.Console.ForegroundColor = System.ConsoleColor.Green;
                        System.Console.Write("Assistant: ");
                        System.Console.ResetColor();
                    }
                    var progress2 = new SyncProgress<string>(c =>
                    {
                        lock (ConsoleLock) System.Console.Write(c);
                    });
                    agent.RunAsync("用户确认继续", progress2, CancellationToken.None).GetAwaiter().GetResult();
                    System.Console.WriteLine();
                }
                continue;
            }
            if (!result.Success && result.ErrorMessage is { } err)
            {
                System.Console.ForegroundColor = System.ConsoleColor.Red;
                System.Console.WriteLine($"Error: {err}");
                System.Console.ResetColor();
            }
        }
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

    /// <summary>IProgress that invokes synchronously (avoids late callbacks that interleave with prompt).</summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SyncProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }
}
