using System.Diagnostics;

namespace OpenLum.Console.Extractors;

/// <summary>
/// Runs bundled text-extraction exes under <c>InternalTools/read/</c> (relative to <see cref="AppContext.BaseDirectory"/>).
/// </summary>
public static class ExeReadDispatcher
{
    /// <summary>Relative to <see cref="AppContext.BaseDirectory"/>.</summary>
    private const string Root = "InternalTools";

    private static readonly Dictionary<string, string> ExtensionToRelativeExe =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = Path.Combine(Root, "read", "pdf", "read-pdf.exe"),
            [".docx"] = Path.Combine(Root, "read", "docx", "read-docx.exe"),
            [".pptx"] = Path.Combine(Root, "read", "pptx", "read-pptx.exe"),
            [".doc"] = Path.Combine(Root, "read", "docppt", "read-docppt.exe"),
            [".ppt"] = Path.Combine(Root, "read", "docppt", "read-docppt.exe"),
            [".dxf"] = Path.Combine(Root, "read", "dxf", "read-dxf.exe"),
            [".dwg"] = Path.Combine(Root, "read", "dwg", "read-dwg.exe"),
        };

    public static bool IsSupportedExtractExtension(string extension) =>
        ExtensionToRelativeExe.ContainsKey(extension);

    /// <summary>Character-based paging: exe args --start and --limit.</summary>
    public static async Task<string> ExtractTextAsync(string fullPath, int start, int limit, CancellationToken ct)
    {
        var ext = Path.GetExtension(fullPath);
        if (!ExtensionToRelativeExe.TryGetValue(ext, out var relativeExe))
        {
            return
                "Error: [read] No bundled extractor is registered for this extension. " +
                "Do not call read for this path; use another approach if the host provides one. " +
                $"Supported extract extensions: {string.Join(", ", ExtensionToRelativeExe.Keys)}";
        }

        var baseDir = AppContext.BaseDirectory;
        var exePath = Path.GetFullPath(Path.Combine(baseDir, relativeExe));
        if (!File.Exists(exePath))
        {
            return
                "Error: [read] Extractor binary is missing from the host install (expected under InternalTools/read/). " +
                "This is a deployment issue, not a bad file. Do not try exec, Python, or ad-hoc converters—restore the bundled exe layout per InternalTools/read/README.txt, then retry.";
        }

        start = Math.Max(0, start);
        limit = Math.Max(1, Math.Min(limit, 2_000_000));

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"\"{fullPath}\" --start {start} --limit {limit}",
            WorkingDirectory = baseDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
            return "Error: [read] Failed to start the extractor process (host/OS). Do not retry with unrelated tools.";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(3));

        try
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? "(no stderr)" : stderr.Trim();
                return
                    $"Error: [read] Extraction failed (exit {process.ExitCode}). " +
                    "The file may be corrupt, encrypted/password-protected, truncated, or not a valid document of this type. " +
                    "Do not try exec, Python, or other workarounds on the same path unless the user supplies a different file or removes protection. " +
                    $"Details: {detail}";
            }

            var combined = stdout ?? "";
            if (!string.IsNullOrWhiteSpace(stderr))
                combined += (combined.Length > 0 ? "\n" : "") + stderr;
            return combined;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return "Error: [read] Extractor timed out. The file may be too large or the host is stuck. Do not chain unrelated tools.";
        }
    }
}
