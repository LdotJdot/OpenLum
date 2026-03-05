using System.Diagnostics;
using System.Text;
using OpenLum.Console.Interfaces;

namespace OpenLum.Console.Tools;

/// <summary>
/// Executes shell commands. Runs in workspace directory.
/// </summary>
public sealed class ExecTool : ITool
{
    private readonly string _workspaceDir;
    private readonly int _defaultTimeoutSeconds;

    public ExecTool(string workspaceDir, int defaultTimeoutSeconds = 60)
    {
        _workspaceDir = Path.GetFullPath(workspaceDir);
        _defaultTimeoutSeconds = defaultTimeoutSeconds;
    }

    public string Name => "exec";
    public string Description => "Run a PowerShell command. Working directory is workspace. Prefer absolute paths for any exec path or working directory; workspace-relative paths are also accepted. Use PowerShell syntax only: chain with ; (not &&), use & \"path\" for paths with spaces. Do NOT use cmd /c or bash-style.";
    public IReadOnlyList<ToolParameter> Parameters =>
    [
        new ToolParameter("command", "string", "PowerShell command. Use ; to chain (never &&). Example: cd \"path\"; dotnet build", true),
        new ToolParameter("timeoutSeconds", "number", "Timeout in seconds (default 60)", false),
        new ToolParameter("stdin", "string", "Input to send to process stdin (for Console.ReadLine/ReadKey). Each line for ReadLine; single chars for ReadKey. Example: \"hello\\n\" or \"y\\n\" for prompts.", false)
    ];

    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var command = args.GetValueOrDefault("command")?.ToString();
        if (string.IsNullOrWhiteSpace(command))
            return "Error: command is required.";

        var timeoutSec = _defaultTimeoutSeconds;
        if (args.TryGetValue("timeoutSeconds", out var v))
        {
            var n = v switch
            {
                System.Text.Json.JsonElement je when je.TryGetInt32(out var i) => i,
                long l => (int)l,
                int i => i,
                _ => 0
            };
            if (n > 0) timeoutSec = Math.Min(n, 600);
        }

        var stdinContent = args.GetValueOrDefault("stdin")?.ToString();

        var workingDir = _workspaceDir;
        if (TryResolveSkillExeWorkingDir(command, out var skillExeDir))
            workingDir = skillExeDir;

        // 校验 skill exe 是否存在，避免 LLM 幻觉调用不存在的文件（如 webbrowser.exe）
        if (TryExtractSkillExePath(command, out var exePath) && !File.Exists(exePath))
        {
            var skillDir = Path.GetDirectoryName(exePath) ?? "";
            var suggestedRead = skillDir.IndexOf("skills", StringComparison.OrdinalIgnoreCase) >= 0
                ? $" 请先 read 该 skill 的 SKILL.md 确认正确的 exe 路径和文件名，切勿根据 skill 名称猜测 exe 名。"
                : "";
            return $"Error: 技能 exe 不存在: {exePath}{suggestedRead}";
        }

        var (fileName, argsStr) = GetShellInvoke(command);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = argsStr,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = !string.IsNullOrEmpty(stdinContent),
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return "Error: failed to start process.";

            if (!string.IsNullOrEmpty(stdinContent))
            {
                await process.StandardInput.WriteAsync(stdinContent);
                process.StandardInput.Close();
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            var outTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errTask = process.StandardError.ReadToEndAsync(cts.Token);

            var completed = await Task.WhenAny(
                process.WaitForExitAsync(cts.Token),
                Task.Delay(TimeSpan.FromSeconds(timeoutSec + 1), cts.Token)
            );

            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return "Error: command timed out.";
            }

            var stdout = await outTask;
            var stderr = await errTask;

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(stdinContent))
            {
                sb.Append("\x1b[34m[stdin]\x1b[0m ");
                sb.AppendLine(stdinContent.Replace("\r", "").Replace("\n", "\\n"));
            }
            if (stdout.Length > 0)
                sb.Append(stdout);
            if (stderr.Length > 0)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append("stderr: ");
                sb.Append(stderr);
            }

            var result = sb.ToString();
            if (result.Length > 50_000)
                result = result[..50_000] + $"\n... [truncated, {result.Length - 50_000} more chars]";

            return string.IsNullOrEmpty(result) ? $"(exit code {process.ExitCode})" : result;
        }
        catch (OperationCanceledException)
        {
            return "Error: command was cancelled or timed out.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Extracts the first exe path from command if it appears to be under skills directory.
    /// Handles both "path.exe" and "&amp; \"path.exe\"" PowerShell styles.
    /// </summary>
    private bool TryExtractSkillExePath(string command, out string fullPath)
    {
        fullPath = "";
        var s = command.AsSpan().Trim();
        if (s.Length == 0) return false;

        // 跳过 & 或 ; 开头
        if (s[0] == '&' || s[0] == ';')
            s = s[1..].Trim();

        var candidate = ExtractPathFromToken(s);
        if (string.IsNullOrEmpty(candidate) || !candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return false;
        if (candidate.IndexOf("skills", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        var bases = new[] { _workspaceDir, AppContext.BaseDirectory };
        foreach (var baseDir in bases)
        {
            var resolved = Path.IsPathRooted(candidate)
                ? Path.GetFullPath(candidate)
                : Path.GetFullPath(Path.Combine(baseDir, candidate.TrimStart('/', '\\')));
            if (resolved.IndexOf("skills", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fullPath = resolved;
                return true;
            }
        }
        fullPath = Path.GetFullPath(Path.Combine(_workspaceDir, candidate.TrimStart('/', '\\')));
        return true;
    }

    /// <summary>
    /// Extracts first path token (handles quoted and unquoted).
    /// </summary>
    private static string ExtractPathFromToken(ReadOnlySpan<char> s)
    {
        if (s.Length == 0) return "";
        var q = s[0] == '"' || s[0] == '\'' ? s[0] : (char)0;
        if (q != 0)
        {
            var end = s[1..].IndexOf(q);
            return end < 0 ? "" : s[1..(end + 1)].ToString();
        }
        var space = s.IndexOf(' ');
        return space < 0 ? s.ToString() : s[..space].ToString();
    }

    /// <summary>
    /// If the command invokes an exe under a skills directory, returns that exe's directory
    /// so native DLLs (e.g. pdfium) can be loaded. Otherwise returns false.
    /// </summary>
    private bool TryResolveSkillExeWorkingDir(string command, out string exeDir)
    {
        exeDir = _workspaceDir;
        var first = ExtractFirstToken(command);
        if (string.IsNullOrWhiteSpace(first) || !first.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return false;
        var bases = new[] { _workspaceDir, AppContext.BaseDirectory };
        foreach (var baseDir in bases)
        {
            var fullPath = Path.IsPathRooted(first)
                ? Path.GetFullPath(first)
                : Path.GetFullPath(Path.Combine(baseDir, first.TrimStart('/', '\\')));
            if (File.Exists(fullPath) && fullPath.IndexOf("skills", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exeDir = Path.GetDirectoryName(fullPath) ?? _workspaceDir;
                return true;
            }
        }
        return false;
    }

    private static string ExtractFirstToken(string command)
    {
        var s = command.AsSpan().Trim();
        if (s.Length == 0) return "";
        if (s[0] == '"')
        {
            var end = s[1..].IndexOf('"');
            return end < 0 ? s.ToString() : s[1..(end + 1)].ToString();
        }
        var space = s.IndexOf(' ');
        return space < 0 ? s.ToString() : s[..space].ToString();
    }

    private static (string FileName, string Arguments) GetShellInvoke(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            // 使用 PowerShell 执行，支持 cmdlet 与 cmd 命令。-EncodedCommand 避免转义问题
            var bytes = System.Text.Encoding.Unicode.GetBytes(command);
            var encoded = Convert.ToBase64String(bytes);
            return ("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}");
        }
        return ("/bin/sh", "-c " + (command.Contains('\'')
            ? "\"" + command.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$") + "\""
            : "'" + command + "'"));
    }

}
