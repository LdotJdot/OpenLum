using System.Text.Json;

namespace OpenLum.Browser;

/// <summary>解析 CLI 参数为 JSON 命令，与 read skill 风格一致：直接传参执行。</summary>
internal static class CliParser
{
    public static (string? Json, string? Error) Parse(string[] args)
    {
        var i = 0;

        // 全局参数（用于 init，或注入到命令中）
        bool? visible = null;
        bool? headless = null;
        string? channel = null;

        while (i < args.Length)
        {
            var a = args[i];
            if (a == "--server" || a == "--master")
                return (null, null); // 由 Main 单独处理为主进程
            if (a == "--visible") { visible = true; i++; continue; }
            if (a == "--headless") { headless = true; i++; continue; }
            if (a == "--channel" && i + 1 < args.Length)
            {
                channel = args[++i];
                i++;
                continue;
            }
            break;
        }

        if (i >= args.Length)
            return (null, "usage: openlum-browser [--visible|--headless] <command> [args]");

        var cmd = args[i++].ToLowerInvariant();
        var dict = new Dictionary<string, object?> { ["cmd"] = cmd };

        if (visible == true) dict["visible"] = true;
        if (headless == true) dict["headless"] = true;
        if (channel != null) dict["channel"] = channel;

        string? err = null;
        switch (cmd)
        {
            case "navigate":
                err = ParseNavigate(args, i, dict);
                break;
            case "snapshot":
                err = ParseSnapshot(args, i, dict);
                break;
            case "click":
                err = ParseClick(args, i, dict);
                break;
            case "type":
                err = ParseType(args, i, dict);
                break;
            case "page_text":
                err = ParsePageText(args, i, dict);
                break;
            case "upload":
                err = ParseUpload(args, i, dict);
                break;
            case "tabs":
                err = ParseTabs(args, i, dict);
                break;
            case "init":
                err = ParseInit(args, i, dict);
                break;
            case "eval":
                err = ParseEval(args, i, dict);
                break;
            case "find":
                err = ParseFind(args, i, dict);
                break;
            case "quit":
                break;
            default:
                return (null, $"unknown command: {cmd}");
        }

        if (err != null) return (null, err);
        return (JsonSerializer.Serialize(dict), null);
    }

    private static string? ParseNavigate(string[] args, int i, Dictionary<string, object?> dict)
    {
        while (i < args.Length)
        {
            if (args[i] == "--url" && i + 1 < args.Length)
            {
                dict["url"] = args[++i];
                i++;
            }
            else if (args[i] == "--visible")
            {
                dict["visible"] = true;
                i++;
            }
            else if (args[i] == "--headless")
            {
                dict["headless"] = true;
                i++;
            }
            else
                i++;
        }
        return dict.ContainsKey("url") ? null : "navigate requires --url";
    }

    private static string? ParseSnapshot(string[] args, int i, Dictionary<string, object?> dict)
    {
        while (i < args.Length)
        {
            if (args[i] == "--maxChars" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n))
            {
                dict["maxChars"] = n;
                i += 2;
            }
            else
                i++;
        }
        return null;
    }

    private static string? ParseClick(string[] args, int i, Dictionary<string, object?> dict)
    {
        while (i < args.Length)
        {
            if (args[i] == "--ref" && i + 1 < args.Length)
            {
                dict["ref"] = args[++i];
                i++;
            }
            else if (args[i] == "--x" && i + 1 < args.Length && double.TryParse(args[i + 1], out var xv))
            {
                dict["x"] = xv;
                i += 2;
            }
            else if (args[i] == "--y" && i + 1 < args.Length && double.TryParse(args[i + 1], out var yv))
            {
                dict["y"] = yv;
                i += 2;
            }
            else if (args[i] == "--force")
            {
                dict["force"] = true;
                i++;
            }
            else
                i++;
        }
        var hasRef = dict.ContainsKey("ref");
        var hasXY = dict.ContainsKey("x") && dict.ContainsKey("y");
        return (hasRef || hasXY) ? null : "click requires --ref or --x and --y";
    }

    private static string? ParseType(string[] args, int i, Dictionary<string, object?> dict)
    {
        while (i < args.Length)
        {
            if (args[i] == "--ref" && i + 1 < args.Length)
            {
                dict["ref"] = args[++i];
                i++;
            }
            else if (args[i] == "--text" && i + 1 < args.Length)
            {
                dict["text"] = args[++i];
                i++;
            }
            else if (args[i] == "--submit")
            {
                dict["submit"] = true;
                i++;
            }
            else
                i++;
        }
        return dict.ContainsKey("ref") && dict.ContainsKey("text") ? null : "type requires --ref and --text";
    }

    private static string? ParsePageText(string[] args, int i, Dictionary<string, object?> dict)
    {
        while (i < args.Length)
        {
            if (args[i] == "--maxChars" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n))
            {
                dict["maxChars"] = n;
                i += 2;
            }
            else
                i++;
        }
        return null;
    }

    private static string? ParseUpload(string[] args, int i, Dictionary<string, object?> dict)
    {
        var paths = new List<string>();
        while (i < args.Length)
        {
            if (args[i] == "--ref" && i + 1 < args.Length)
            {
                dict["ref"] = args[++i];
                i++;
            }
            else if (args[i] == "--paths")
            {
                i++;
                while (i < args.Length && !args[i].StartsWith("--"))
                    paths.Add(args[i++]);
            }
            else
                i++;
        }
        dict["paths"] = paths;
        return dict.ContainsKey("ref") && paths.Count > 0 ? null : "upload requires --ref and --paths <path1> [path2...]";
    }

    private static string? ParseTabs(string[] args, int i, Dictionary<string, object?> dict)
    {
        while (i < args.Length)
        {
            if (args[i] == "--switch" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n))
            {
                dict["switch"] = n;
                i += 2;
            }
            else
                i++;
        }
        return null;
    }

    private static string? ParseInit(string[] args, int i, Dictionary<string, object?> dict)
    {
        while (i < args.Length)
        {
            if (args[i] == "--visible")
            {
                dict["visible"] = true;
                i++;
            }
            else if (args[i] == "--headless")
            {
                dict["headless"] = true;
                i++;
            }
            else if (args[i] == "--channel" && i + 1 < args.Length)
            {
                dict["channel"] = args[++i];
                i++;
            }
            else
                i++;
        }
        return null;
    }

    private static string? ParseEval(string[] args, int i, Dictionary<string, object?> dict)
    {
        while (i < args.Length)
        {
            if (args[i] == "--expr" && i + 1 < args.Length)
            {
                dict["expr"] = args[++i];
                i++;
            }
            else if (args[i] == "--maxChars" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n))
            {
                dict["maxChars"] = n;
                i += 2;
            }
            else
            {
                i++;
            }
        }
        return dict.ContainsKey("expr") ? null : "eval requires --expr <JS_EXPRESSION>";
    }

    private static string? ParseFind(string[] args, int i, Dictionary<string, object?> dict)
    {
        while (i < args.Length)
        {
            if (args[i] == "--text" && i + 1 < args.Length)
            {
                dict["text"] = args[++i];
                i++;
            }
            else if (args[i] == "--role" && i + 1 < args.Length)
            {
                dict["role"] = args[++i];
                i++;
            }
            else if (args[i] == "--limit" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n))
            {
                dict["limit"] = n;
                i += 2;
            }
            else
                i++;
        }
        return (dict.ContainsKey("text") || dict.ContainsKey("role")) ? null : "find requires --text and/or --role";
    }
}
