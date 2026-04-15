using System.Text.RegularExpressions;
using OpenLum.Console.Config;

namespace OpenLum.Console.Agent;

/// <summary>
/// Decides whether to insert a no-tool "instruction thinking" round right after the user message (before Observe/Act).
/// </summary>
public static class InstructionThinkingHeuristic
{
    /// <summary>Run instruction-prep when workflow uses this mode and heuristics match.</summary>
    public static bool ShouldRun(string? userPrompt, WorkflowConfig wf)
    {
        var mode = (wf.InstructionThinking ?? "auto").Trim().ToLowerInvariant();
        if (mode == "never")
            return false;
        if (mode == "always")
            return true;

        // auto
        var p = (userPrompt ?? "").Trim();
        if (p.Length == 0)
            return false;

        var minChars = Math.Clamp(wf.InstructionThinkingMinChars, 50, 10_000);
        if (p.Length >= minChars)
            return true;

        var lineCount = p.Split(['\r', '\n'], StringSplitOptions.None).Length;
        if (lineCount >= 6)
            return true;

        // Multiple numbered/bulleted lines often mean multi-step work
        var bulletish = Regex.Matches(p, @"^\s*([-*•]|\d+[\.)])\s+", RegexOptions.Multiline).Count;
        if (bulletish >= 3)
            return true;

        string[] ambiguous =
        [
            "或者", "还是", "哪个", "哪些", "是否", "可能", "大概", "尽量", "试试", "不确定",
            "?", "？", "help", "or ", " vs ", "either", "unclear", "ambiguous", "not sure"
        ];
        foreach (var k in ambiguous)
        {
            if (p.Contains(k, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        string[] iterative =
        [
            "多次", "反复", "不断", "调试", "迭代", "试错", "重试", "逐步",
            "retry", "iterate", "trial", "debug", "several times", "step by step"
        ];
        foreach (var k in iterative)
        {
            if (p.Contains(k, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
