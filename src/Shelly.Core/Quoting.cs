using System.Text;

namespace Shelly.Core;

public static class Quoting
{
    /// <summary>Argument for the Windows command line (CommandLineToArgvW rules), always quoted.</summary>
    public static string NativeArg(string s)
    {
        var sb = new StringBuilder("\"");
        int bs = 0;
        foreach (char ch in s)
        {
            if (ch == '\\') { bs++; continue; }
            if (ch == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); }
            else { sb.Append('\\', bs); sb.Append(ch); }
            bs = 0;
        }
        sb.Append('\\', bs * 2);
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>Single-quoted PowerShell string (typographic single quotes are quotes in PowerShell too).</summary>
    public static string PsString(string s)
    {
        var sb = new StringBuilder("'");
        foreach (char ch in s)
        {
            if (ch == '\'' || ch == '\u2018' || ch == '\u2019' || ch == '\u201A' || ch == '\u201B') sb.Append(ch);
            sb.Append(ch);
        }
        return sb.Append('\'').ToString();
    }

    /// <summary>String literal for C# source.</summary>
    public static string CsString(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
