using System.Text;

namespace Shelly.Services;

/// <summary>Shelly's own log (%TEMP%\Shelly\shelly.log): builds, errors. Kept small.</summary>
public static class AppLog
{
    static readonly object gate = new();
    public static string? FilePath;
    public static event Action<string>? Written;

    public static void Write(string text)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text;
        lock (gate)
        {
            try
            {
                if (FilePath != null)
                {
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
        Written?.Invoke(line);
    }

    public static void Error(string where, Exception ex) => Write("! " + where + ": " + ex);
}
