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

        var model = new OpenAIModelProvider(
            apiKey,
            model: config.Model.Model,
            baseUrl: config.Model.BaseUrl
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
            }
        );

        System.Console.WriteLine("OpenLum Console — generic agent shell. Use /help for commands.");
        System.Console.WriteLine();

        return RunRepl(agent, session, config.UserTimezone);
    }

    private static int RunRepl(AgentLoop agent, ConsoleSession session, string? userTimezone = null)
    {
        while (true)
        {
            System.Console.Write("> ");
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

            // Use synchronous progress: Progress<T>.Report() posts to thread pool and can run
            // after RunAsync returns, causing "> " and trailing text to interleave.
            var progress = new SyncProgress<string>(c =>
            {
                lock (ConsoleLock)
                {
                    System.Console.Write(c);
                }
            });
            var result = agent
                .RunAsync(stampedInput, progress, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            System.Console.WriteLine();
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
