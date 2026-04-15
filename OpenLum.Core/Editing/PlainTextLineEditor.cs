using System.Text;
using System.Text.RegularExpressions;

namespace OpenLum.Console.Editing;

/// <summary>
/// Line-based edits (1-based line numbers), UTF-8. Shared library; hosts supply workspace paths.
/// </summary>
public static class PlainTextLineEditor
{
    public static string ReadRange(string fullPath, int startLine, int endLine)
    {
        var lines = File.ReadAllLines(fullPath);
        if (lines.Length == 0)
            return "(empty file)";

        startLine = Math.Clamp(startLine, 1, lines.Length);
        endLine = Math.Clamp(endLine, startLine, lines.Length);
        var sb = new StringBuilder();
        for (var i = startLine - 1; i <= endLine - 1 && i < lines.Length; i++)
            sb.AppendLine($"{i + 1,6}|{lines[i]}");
        return sb.ToString().TrimEnd();
    }

    public static void ReplaceRangeWithText(string fullPath, int startLine, int endLine, IReadOnlyList<string> newLines)
    {
        var lines = File.ReadAllLines(fullPath).ToList();
        var enc = new UTF8Encoding(false);
        if (lines.Count == 0)
        {
            File.WriteAllLines(fullPath, newLines, enc);
            return;
        }

        startLine = Math.Max(1, startLine);
        endLine = Math.Max(startLine, endLine);
        var s = startLine - 1;
        var e = endLine - 1;
        e = Math.Min(e, lines.Count - 1);
        var removeCount = e - s + 1;
        if (s >= lines.Count)
            lines.AddRange(newLines);
        else
        {
            lines.RemoveRange(s, removeCount);
            lines.InsertRange(s, newLines);
        }

        File.WriteAllLines(fullPath, lines, enc);
    }

    public static void ReplaceAll(string fullPath, string oldText, string newText)
    {
        var content = File.ReadAllText(fullPath);
        var effective = TextMatchHelpers.ResolveOldString(content, oldText);
        if (effective is null)
            throw new InvalidOperationException("old_text not found.");
        content = content.Replace(effective, newText, StringComparison.Ordinal);
        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
    }

    public static void ReplaceFirst(string fullPath, string oldText, string newText)
    {
        var content = File.ReadAllText(fullPath);
        var effective = TextMatchHelpers.ResolveOldString(content, oldText);
        if (effective is null)
            throw new InvalidOperationException("old_text not found.");
        var idx = content.IndexOf(effective, StringComparison.Ordinal);
        content = content.Substring(0, idx) + newText + content[(idx + effective.Length)..];
        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
    }

    public static void ReplaceAllRegex(string fullPath, string pattern, string replacement)
    {
        var content = File.ReadAllText(fullPath);
        content = Regex.Replace(content, pattern, replacement);
        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
    }

    public static void InsertLinesAfter(string fullPath, int afterLine, IReadOnlyList<string> insertLines)
    {
        var lines = File.ReadAllLines(fullPath).ToList();
        afterLine = Math.Clamp(afterLine, 0, lines.Count);
        for (var i = insertLines.Count - 1; i >= 0; i--)
            lines.Insert(afterLine, insertLines[i]);
        File.WriteAllLines(fullPath, lines, new UTF8Encoding(false));
    }

    public static void DeleteRange(string fullPath, int startLine, int endLine)
    {
        var lines = File.ReadAllLines(fullPath).ToList();
        if (lines.Count == 0) return;

        startLine = Math.Clamp(startLine, 1, lines.Count);
        endLine = Math.Clamp(endLine, startLine, lines.Count);
        var s = startLine - 1;
        var e = endLine - 1;
        lines.RemoveRange(s, e - s + 1);
        File.WriteAllLines(fullPath, lines, new UTF8Encoding(false));
    }

    public static void AppendLines(string fullPath, IReadOnlyList<string> appendLines)
    {
        using var sw = new StreamWriter(fullPath, append: true, new UTF8Encoding(false));
        foreach (var line in appendLines)
            sw.WriteLine(line);
    }

    public static IReadOnlyList<string> SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();
        return text.Replace("\r\n", "\n").Split('\n');
    }
}
