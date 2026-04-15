using System.Globalization;
using System.Text;

namespace OpenLum.Console.Editing;

/// <summary>
/// Shared exact-match resolution when line endings or pasted text differ from the file.
/// </summary>
internal static class TextMatchHelpers
{
    /// <summary>Returns <paramref name="oldStr"/> or an equivalent that exists in <paramref name="fileContent"/>; null if no match.</summary>
    public static string? ResolveOldString(string fileContent, string oldStr)
    {
        if (string.IsNullOrEmpty(oldStr))
            return null;
        if (fileContent.Contains(oldStr, StringComparison.Ordinal))
            return oldStr;
        foreach (var variant in LineEndingVariants(oldStr, fileContent))
        {
            if (fileContent.Contains(variant, StringComparison.Ordinal))
                return variant;
        }

        var unicodeRelaxed = TryUnescapeLiteralJsonStyleUnicode(oldStr);
        if (unicodeRelaxed is not null &&
            !string.Equals(unicodeRelaxed, oldStr, StringComparison.Ordinal) &&
            fileContent.Contains(unicodeRelaxed, StringComparison.Ordinal))
            return unicodeRelaxed;

        return null;
    }

    /// <summary>
    /// Models sometimes pass <c>\u003c</c> as six literal characters instead of <c>&lt;</c>.
    /// Only sequences where the leading <c>\</c> is not itself escaped (odd count of <c>\</c> before it) are decoded.
    /// </summary>
    internal static string? TryUnescapeLiteralJsonStyleUnicode(string s)
    {
        if (s.Length < 6 || !s.Contains("\\u", StringComparison.Ordinal))
            return null;

        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length;)
        {
            if (i + 5 < s.Length &&
                s[i] == '\\' &&
                (s[i + 1] == 'u' || s[i + 1] == 'U') &&
                IsHex4(s.AsSpan(i + 2, 4)) &&
                CountPrecedingBackslashes(s, i) % 2 == 0)
            {
                var code = ushort.Parse(s.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier);
                sb.Append((char)code);
                i += 6;
            }
            else
            {
                sb.Append(s[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    private static int CountPrecedingBackslashes(string s, int index)
    {
        var n = 0;
        for (var j = index - 1; j >= 0 && s[j] == '\\'; j--)
            n++;
        return n;
    }

    private static bool IsHex4(ReadOnlySpan<char> span)
    {
        if (span.Length != 4) return false;
        foreach (var c in span)
        {
            var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!ok) return false;
        }

        return true;
    }

    /// <summary>Alternate forms of <paramref name="oldStr"/> when line endings differ.</summary>
    public static IEnumerable<string> LineEndingVariants(string oldStr, string fileContent)
    {
        if (string.IsNullOrEmpty(oldStr))
            yield break;

        if (fileContent.Contains("\r\n", StringComparison.Ordinal) && oldStr.Contains('\n') &&
            !oldStr.Contains("\r\n", StringComparison.Ordinal))
            yield return oldStr.Replace("\n", "\r\n");

        if (!fileContent.Contains("\r\n", StringComparison.Ordinal) &&
            oldStr.Contains("\r\n", StringComparison.Ordinal))
            yield return oldStr.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static int CountOccurrences(string text, string search)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(search, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += search.Length;
        }

        return count;
    }
}
