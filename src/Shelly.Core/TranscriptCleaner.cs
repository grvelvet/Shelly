using System.Text;

namespace Shelly.Core;

/// <summary>1.4.0: Strips PowerShell Start-Transcript / Stop-Transcript header and footer blocks from captured window logs.</summary>
public static class TranscriptCleaner
{
    public const string Separator = "**********************";

    /// <summary>Removes the PowerShell transcript header and footer blocks surrounded by <c>**********************</c>
    /// so that only the script's actual console output remains.</summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int start = 0;
        int end = lines.Length;

        // Strip leading empty lines
        while (start < end && string.IsNullOrWhiteSpace(lines[start])) start++;

        // Strip leading transcript header block(s):
        // **********************
        // Windows PowerShell transcript start / PowerShell transcript start (or localized equivalent)
        // Start time: ...
        // ...
        // **********************
        while (start < end && lines[start].Trim() == Separator)
        {
            int closeIdx = -1;
            for (int i = start + 1; i < Math.Min(end, start + 25); i++)
            {
                if (lines[i].Trim() == Separator)
                {
                    closeIdx = i;
                    break;
                }
            }
            if (closeIdx > start && LooksLikeHeaderOrFooter(lines, start + 1, closeIdx))
            {
                start = closeIdx + 1;
                while (start < end && string.IsNullOrEmpty(lines[start])) start++;
            }
            else break;
        }

        // Trim trailing empty lines
        while (end > start && string.IsNullOrWhiteSpace(lines[end - 1])) end--;

        // Strip trailing transcript footer block(s):
        // **********************
        // Windows PowerShell transcript end / PowerShell transcript end (or localized equivalent)
        // End time: ...
        // **********************
        while (end > start && lines[end - 1].Trim() == Separator)
        {
            int openIdx = -1;
            for (int i = end - 2; i >= Math.Max(start, end - 12); i--)
            {
                if (lines[i].Trim() == Separator)
                {
                    openIdx = i;
                    break;
                }
            }
            if (openIdx >= start && LooksLikeHeaderOrFooter(lines, openIdx + 1, end - 1))
            {
                end = openIdx;
                while (end > start && string.IsNullOrWhiteSpace(lines[end - 1])) end--;
            }
            else break;
        }

        if (start >= end) return "";
        return string.Join(Environment.NewLine, lines[start..end]);
    }

    static bool LooksLikeHeaderOrFooter(string[] lines, int from, int to)
    {
        if (to <= from) return false;
        int metaLines = 0;
        for (int i = from; i < to; i++)
        {
            string l = lines[i].Trim();
            if (l.Length == 0) continue;
            if (l.Contains(':') ||
                l.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("transcript", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("транскрип", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("записи", StringComparison.OrdinalIgnoreCase))
            {
                metaLines++;
            }
        }
        return metaLines > 0;
    }

    /// <summary>Caps the cleaned transcript to <paramref name="maxKB"/> kilobytes (keeping the tail if truncated).</summary>
    public static string Cap(string cleaned, int maxKB)
    {
        if (string.IsNullOrEmpty(cleaned) || maxKB <= 0) return cleaned ?? "";
        int maxChars = (int)Math.Min((long)maxKB * 1024L, int.MaxValue / 2);
        if (cleaned.Length <= maxChars) return cleaned;
        string tail = cleaned[^maxChars..];
        int nl = tail.IndexOf('\n');
        if (nl >= 0 && nl < 256) tail = tail[(nl + 1)..];
        return "...\n" + tail;
    }

    public static (string Output, bool Truncated) Cap(string? raw, int maxKB, string? truncatedNote)
    {
        string cleaned = Clean(raw);
        if (string.IsNullOrEmpty(cleaned) || maxKB <= 0) return (cleaned, false);
        int maxChars = (int)Math.Min((long)maxKB * 1024L, int.MaxValue / 2);
        if (cleaned.Length <= maxChars) return (cleaned, false);
        string capped = cleaned[..maxChars];
        if (!string.IsNullOrEmpty(truncatedNote))
            capped += Environment.NewLine + truncatedNote;
        return (capped, true);
    }

    /// <summary>Reads the raw contents of a transcript file that may still be open by PowerShell (<see cref="FileShare.ReadWrite"/>).</summary>
    public static string ReadRawLogFile(string? logPath)
    {
        if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath)) return "";
        try
        {
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return sr.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Reads a transcript file that may still be open by PowerShell (<see cref="FileShare.ReadWrite"/>), cleans and caps it.</summary>
    public static string ReadLogFile(string? logPath, int maxKB = 64)
    {
        string raw = ReadRawLogFile(logPath);
        if (raw.Length == 0) return "";
        return Cap(Clean(raw), maxKB);
    }
}
