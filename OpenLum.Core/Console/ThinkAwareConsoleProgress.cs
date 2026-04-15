using System.Text;

namespace OpenLum.Console.Console;

/// <summary>
/// Wraps streamed assistant output: content inside <thinking>...</thinking> (or legacy <think>...</think>) is printed in yellow.
/// The tags themselves are not printed. Ensures think block is visually distinct like in web UIs.
/// </summary>
public sealed class ThinkAwareConsoleProgress : IProgress<string>
{
    // Support both legacy <think>...</think> and unified <thinking>...</thinking> tags.
    private const string OpenTagShort = "<think>";
    private const string CloseTagShort = "</think>";
    private const string OpenTagLong = "<thinking>";
    private const string CloseTagLong = "</thinking>";
    private const int OpenTagShortLen = 7; // "<think>".Length
    private const int CloseTagShortLen = 8; // "</think>".Length
    private const int OpenTagLongLen = 10; // "<thinking>".Length
    private const int CloseTagLongLen = 11; // "</thinking>".Length

    private readonly object _consoleLock;
    private readonly StringBuilder _buffer = new();
    private bool _insideThink;
    private bool _thinkBlockStarted; // have we already printed newline + yellow for current block

    public ThinkAwareConsoleProgress(object consoleLock)
    {
        _consoleLock = consoleLock;
    }

    public void Report(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        lock (_consoleLock)
        {
            _buffer.Append(value);
            Flush();
        }
    }

    private void Flush()
    {
        while (true)
        {
            if (_insideThink)
            {
                var (closeIdx, closeLen) = IndexOfAnyCaseInsensitive(
                    _buffer,
                    CloseTagShort,
                    CloseTagLong
                );
                if (closeIdx >= 0)
                {
                    // Content before </think>
                    WriteThinkContent(_buffer.ToString(0, closeIdx));
                    _buffer.Remove(0, closeIdx + closeLen);
                    EndThinkBlock();
                    _insideThink = false;
                    continue;
                }
                // Keep last (maxCloseLen - 1) chars in case tag is split
                int maxCloseLen = Math.Max(CloseTagShortLen, CloseTagLongLen);
                int keep = maxCloseLen - 1;
                if (_buffer.Length > keep)
                {
                    WriteThinkContent(_buffer.ToString(0, _buffer.Length - keep));
                    _buffer.Remove(0, _buffer.Length - keep);
                }
                break;
            }
            else
            {
                var (openIdx, openLen) = IndexOfAnyCaseInsensitive(
                    _buffer,
                    OpenTagShort,
                    OpenTagLong
                );
                if (openIdx >= 0)
                {
                    WriteNormal(_buffer.ToString(0, openIdx));
                    _buffer.Remove(0, openIdx + openLen);
                    StartThinkBlock();
                    _insideThink = true;
                    continue;
                }
                int maxOpenLen = Math.Max(OpenTagShortLen, OpenTagLongLen);
                int keep = maxOpenLen - 1;
                if (_buffer.Length > keep)
                {
                    WriteNormal(_buffer.ToString(0, _buffer.Length - keep));
                    _buffer.Remove(0, _buffer.Length - keep);
                }
                break;
            }
        }
    }

    /// <summary>Call when stream ends so any remaining buffer is written.</summary>
    public void FlushRemaining()
    {
        lock (_consoleLock)
        {
            if (_buffer.Length == 0)
                return;
            if (_insideThink)
            {
                // 流结束时还有未刷新的思考内容，一并输出并收尾颜色。
                WriteThinkContent(_buffer.ToString());
                EndThinkBlock();
                _insideThink = false;
            }
            else
            {
                WriteNormal(_buffer.ToString());
            }
            _buffer.Clear();
        }
    }

    private void StartThinkBlock()
    {
        System.Console.WriteLine();
        System.Console.ForegroundColor = System.ConsoleColor.DarkYellow;
        _thinkBlockStarted = true;
    }

    private void WriteThinkContent(string s)
    {
        // 显示思考内容本身，但使用黄色高亮，与正常输出区分开。
        if (!_thinkBlockStarted)
        {
            System.Console.WriteLine();
            System.Console.ForegroundColor = System.ConsoleColor.DarkYellow;
            _thinkBlockStarted = true;
        }
        System.Console.Write(s);
    }

    private void EndThinkBlock()
    {
        if (_thinkBlockStarted)
        {
            System.Console.WriteLine();
            System.Console.ResetColor();
        }
        _thinkBlockStarted = false;
    }

    private void WriteNormal(string s)
    {
        System.Console.Write(s);
    }

    private static (int Index, int Length) IndexOfAnyCaseInsensitive(
        StringBuilder sb,
        string value1,
        string value2
    )
    {
        int bestIndex = -1;
        int bestLen = 0;

        void TryMatch(string v)
        {
            if (v.Length == 0 || sb.Length < v.Length)
                return;
            for (int i = 0; i <= sb.Length - v.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < v.Length; j++)
                {
                    if (char.ToLowerInvariant(sb[i + j]) != char.ToLowerInvariant(v[j]))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    if (bestIndex == -1 || i < bestIndex)
                    {
                        bestIndex = i;
                        bestLen = v.Length;
                    }
                    return;
                }
            }
        }

        TryMatch(value1);
        TryMatch(value2);
        return (bestIndex, bestLen);
    }
}
