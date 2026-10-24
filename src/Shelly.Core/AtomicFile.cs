using System.Text;

namespace Shelly.Core;

/// <summary>Writes a file so that a crash or a full disk never leaves half of it: the text goes to a temporary file first,
/// which then replaces the target in one step. The previous version is kept next to it as .bak.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string text, Encoding encoding)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text, encoding);
        if (File.Exists(path))
        {
            try { File.Replace(tmp, path, path + ".bak"); return; }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException) { }   // e.g. a file system without Replace: plain move below
        }
        File.Move(tmp, path, true);
    }
}
