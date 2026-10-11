using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;
using Shelly.Core;

namespace Shelly.Services;

/// <summary>Taskbar jump list: favorites start Shelly with --run "Folder\Script.ps1".</summary>
public static class JumpLists
{
    public static void Update()
    {
        try
        {
            var st = App.S;
            var jl = new JumpList { ShowFrequentCategory = false, ShowRecentCategory = false };
            if (st.Settings.JumpList)
            {
                string exe = Environment.ProcessPath!;
                foreach (var rel in st.Settings.Favorites.Take(20))
                {
                    var f = st.Find(rel);
                    if (f == null) continue;
                    string? icon = st.IconPath(rel);
                    jl.JumpItems.Add(new JumpTask
                    {
                        Title = f.Name,
                        Description = Loc.F("jumpRunTip", f.Name),
                        ApplicationPath = exe,
                        Arguments = "--run " + Quoting.NativeArg(f.Rel),
                        IconResourcePath = icon != null && icon.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ? icon : exe,
                        CustomCategory = Loc.T("jumpFavorites"),
                        WorkingDirectory = st.Paths.Root
                    });
                }
            }
            JumpList.SetJumpList(Application.Current, jl);
            jl.Apply();
        }
        catch (Exception ex) { AppLog.Error("jump list", ex); }
    }
}

/// <summary>Start with Windows: HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Shelly.</summary>
public static class AutoStart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string Command => Quoting.NativeArg(Environment.ProcessPath!) + " --minimized";

    /// <summary>null = off; "" = on for this exe; otherwise the other command found.</summary>
    public static string? Current()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            if (k?.GetValue("Shelly") is not string v) return null;
            return v.Contains(Environment.ProcessPath!, StringComparison.OrdinalIgnoreCase) ? "" : v;
        }
        catch { return null; }
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key, true);
        if (on) k.SetValue("Shelly", Command);
        else k.DeleteValue("Shelly", false);
    }
}

/// <summary>Script icons: pictures are copied to assets\script-icons; PNG is wrapped into an .ico for exe builds.</summary>
public static class IconUtil
{
    static readonly Dictionary<string, (DateTime, ImageSource?)> cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Load(string? path, int size = 32)
    {
        if (path == null) return null;
        try
        {
            var t = File.GetLastWriteTimeUtc(path);
            string key = path + "|" + size;
            if (cache.TryGetValue(key, out var c) && c.Item1 == t) return c.Item2;
            ImageSource? img;
            if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var fs = File.OpenRead(path);
                var dec = new IconBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                img = dec.Frames.OrderBy(fr => Math.Abs(fr.PixelWidth - size * 2)).FirstOrDefault();
            }
            else
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bi.UriSource = new Uri(path);
                bi.DecodePixelWidth = size * 2;
                bi.EndInit();
                img = bi;
            }
            img?.Freeze();
            cache[key] = (t, img);
            return img;
        }
        catch { return null; }
    }

    /// <summary>Copy a picture for a script into assets\script-icons and return its path relative to the Shelly folder.</summary>
    public static string Import(AppPaths paths, string rel, string source)
    {
        Directory.CreateDirectory(paths.IconsDir);
        string ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext != ".ico" && ext != ".png") throw new InvalidOperationException(ext);
        // Validate that it decodes
        if (Load(source) == null) throw new InvalidOperationException(Path.GetFileName(source));
        string safe = string.Concat(rel.Replace('\\', '_').Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        if (safe.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) safe = safe[..^4];
        string dst = Path.Combine(paths.IconsDir, safe + ext);
        foreach (var e in new[] { ".ico", ".png" }) { try { File.Delete(Path.Combine(paths.IconsDir, safe + e)); } catch { } }
        File.Copy(source, dst, true);
        return Path.Combine("assets", "script-icons", safe + ext);
    }

    /// <summary>An .ico for csc /win32icon: the .ico itself, or a PNG wrapped into a single-image .ico (256 px max).</summary>
    public static string? IcoForBuild(string? path)
    {
        if (path == null) return null;
        if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) return path;
        try
        {
            var src = new BitmapImage();
            src.BeginInit();
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.UriSource = new Uri(path);
            src.EndInit();
            int size = Math.Min(256, Math.Max(src.PixelWidth, src.PixelHeight));
            if (size < 16) size = 16;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double s = Math.Min((double)size / src.PixelWidth, (double)size / src.PixelHeight);
                double w = src.PixelWidth * s, h = src.PixelHeight * s;
                dc.DrawImage(src, new Rect((size - w) / 2, (size - h) / 2, w, h));
            }
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var png = new MemoryStream();
            enc.Save(png);
            byte[] data = png.ToArray();
            string ico = Path.Combine(Path.GetTempPath(), "ShellyBuild", "icon-" + Guid.NewGuid().ToString("N") + ".ico");
            Directory.CreateDirectory(Path.GetDirectoryName(ico)!);
            using var fs = File.Create(ico);
            using var bw = new BinaryWriter(fs);
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)1);
            bw.Write((byte)(size >= 256 ? 0 : size)); bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
            bw.Write(data.Length); bw.Write(22);
            bw.Write(data);
            return ico;
        }
        catch { return null; }
    }
}

/// <summary>Update check against GitHub Releases: only tells you and links the download.</summary>
public static class Updates
{
    public static UpdateChecker.Release? Latest { get; private set; }
    public static event Action? Found;

    public static async Task<string> CheckAsync(bool manual)
    {
        var st = App.S;
        if (!UpdateChecker.IsRepoSet(AppState.Repo)) return Loc.T("updNoRepo");
        if (!manual && (!st.Settings.UpdateCheck || !UpdateChecker.Due(st.Settings.UpdateLastCheck, DateTime.UtcNow)))
        {
            // Remembered result of the last check
            if (!manual && st.Settings.UpdateLatest.Length > 0 && UpdateChecker.IsNewer(st.Settings.UpdateLatest, AppState.Version))
            {
                Latest = new UpdateChecker.Release(st.Settings.UpdateLatest, "v" + st.Settings.UpdateLatest,
                    "https://github.com/" + AppState.Repo + "/releases/latest", "Shelly " + st.Settings.UpdateLatest, null);
                Found?.Invoke();
            }
            return "";
        }
        try
        {
            var r = await UpdateChecker.GetLatestAsync(AppState.Repo);
            st.Settings.UpdateLastCheck = DateTime.UtcNow.ToString("o");
            st.Settings.UpdateLatest = r?.Version ?? "";
            st.SaveSettings();
            if (r != null && UpdateChecker.IsNewer(r.Version, AppState.Version))
            {
                Latest = r;
                Found?.Invoke();
                return Loc.F("updAvailable", r.Version, AppState.Version);
            }
            return Loc.F("updNone", AppState.Version);
        }
        catch (Exception ex)
        {
            AppLog.Write("update check: " + ex.Message);
            return Loc.F("updFail", ex.Message);
        }
    }

    public static void OpenDownload()
    {
        if (Latest == null) return;
        ShellOps.Open(Latest.AssetUrl ?? Latest.Url);
    }
}

/// <summary>Things the compatibility page and the info bar check.</summary>
public static class SystemInfo
{
    public static int WindowsBuild => Environment.OSVersion.Version.Build;

    public static string? NetFrameworkVersion()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            if (k?.GetValue("Release") is not int r) return null;
            return r >= 533320 ? "4.8.1" : r >= 528040 ? "4.8" : r >= 461808 ? "4.7.2" : r >= 461308 ? "4.7.1" : r >= 460798 ? "4.7" :
                   r >= 394802 ? "4.6.2" : r >= 394254 ? "4.6.1" : r >= 393295 ? "4.6" : r >= 379893 ? "4.5.2" : r >= 378675 ? "4.5.1" : r >= 378389 ? "4.5" : "4.0";
        }
        catch { return null; }
    }

    /// <summary>Execution policy set by Group Policy (machine or user), or null.</summary>
    public static string? PolicyExecution()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var k = hive.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\PowerShell");
                if (k == null) continue;
                if (k.GetValue("EnableScripts") is int en && en == 0) return "Restricted";
                if (k.GetValue("ExecutionPolicy") is string p && p.Length > 0) return p;
            }
            catch { }
        }
        return null;
    }

    public static void RestartShelly()
    {
        try
        {
            App.Quit();
            if (App.Exiting) Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restarted") { UseShellExecute = true });
        }
        catch (Exception ex) { App.Status(Loc.F("setRestartFail", ex.Message), true); }
    }
}
