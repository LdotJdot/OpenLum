using System.Text;
using System.Text.RegularExpressions;

namespace OpenLum.Console.Tools;

internal static class GlobRegex
{
    /// <summary>
    /// Convert a glob pattern to a regex. Supports *, **, ?, {a,b}.
    /// Patterns without path separators get auto-prepended with **/ for recursive matching.
    /// </summary>
    public static Regex ToRegex(string glob)
    {
        if (!glob.Contains('/') && !glob.Contains('\\') && !glob.StartsWith("**/"))
            glob = "**/" + glob;

        var sb = new StringBuilder("^");
        var i = 0;
        while (i < glob.Length)
        {
            var c = glob[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        if (i + 2 < glob.Length && (glob[i + 2] == '/' || glob[i + 2] == '\\'))
                        {
                            sb.Append("(.+/)?");
                            i += 3;
                        }
                        else
                        {
                            sb.Append(".*");
                            i += 2;
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                        i++;
                    }
                    break;
                case '?':
                    sb.Append("[^/]");
                    i++;
                    break;
                case '{':
                    var end = glob.IndexOf('}', i);
                    if (end > i)
                    {
                        var alts = glob[(i + 1)..end].Split(',');
                        sb.Append('(');
                        sb.Append(string.Join('|', alts.Select(Regex.Escape)));
                        sb.Append(')');
                        i = end + 1;
                    }
                    else
                    {
                        sb.Append(Regex.Escape(c.ToString()));
                        i++;
                    }
                    break;
                case '\\':
                case '/':
                    sb.Append("[/\\\\]");
                    i++;
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    i++;
                    break;
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}

