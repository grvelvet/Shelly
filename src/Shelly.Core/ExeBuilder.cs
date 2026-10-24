using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Shelly.Core;

/// <summary>Builds a console .exe that carries the script and runs it with PowerShell, using the C# compiler of
/// .NET Framework 4 (csc.exe, part of every Windows 10/11): nothing to install.</summary>
public static class ExeBuilder
{
    public sealed record Result(bool Ok, string Log);

    public static string? FindCsc()
    {
        string? windir = Environment.GetEnvironmentVariable("windir");
        if (string.IsNullOrEmpty(windir)) return null;
        foreach (var fw in new[] { "Framework64", "Framework" })
        {
            string p = Path.Combine(windir, "Microsoft.NET", fw, "v4.0.30319", "csc.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    static string? stub;
    public static string StubTemplate
    {
        get
        {
            if (stub != null) return stub;
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shelly.ExeStub.cs")!;
            using var rd = new StreamReader(s, Encoding.UTF8);
            return stub = rd.ReadToEnd();
        }
    }

    /// <summary>The C# source for a script exe (engine "7" | "5.1" | "auto").</summary>
    public static string MakeSource(byte[] script, string engine, string version)
    {
        string b64 = Convert.ToBase64String(script);
        var chunks = new List<string>();
        for (int i = 0; i < b64.Length; i += 120) chunks.Add("\"" + b64.Substring(i, Math.Min(120, b64.Length - i)) + "\"");
        if (chunks.Count == 0) chunks.Add("\"\"");
        if (engine != "auto" && engine != "7" && engine != "5.1") engine = "auto";
        return StubTemplate
            .Replace("%SCRIPT%", string.Join(" +\r\n        ", chunks))
            .Replace("%VERSION%", version)
            .Replace("%MSG_PRESSKEY%", Quoting.CsString(Loc.T("stubPressKey")))
            .Replace("%MSG_TEMPFAIL%", Quoting.CsString(Loc.T("stubTempFail")))
            .Replace("%MSG_PSFAIL%", Quoting.CsString(Loc.T("stubPsFail")))
            .Replace("%MSG_ENGINEFALLBACK%", Quoting.CsString(Loc.T("stubEngineFallback")))
            .Replace("%ENGINE%", Quoting.CsString(engine));
    }

    public static Result Build(string scriptPath, string outPath, string engine, string? iconPath, string version)
    {
        string? csc = FindCsc();
        if (csc == null) return new(false, Loc.T("cscMissing"));
        string tmp = Path.Combine(Path.GetTempPath(), "ShellyBuild");
        Directory.CreateDirectory(tmp);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        string cs = Path.Combine(tmp, "stub-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(cs, MakeSource(File.ReadAllBytes(scriptPath), engine, version), new UTF8Encoding(true));
        string bas = "/nologo /utf8output /nowarn:169 /optimize+ /platform:anycpu /target:exe /r:System.Windows.Forms.dll /out:" + Quoting.NativeArg(outPath);
        var attempts = new List<string>();
        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath)) attempts.Add(bas + " /win32icon:" + Quoting.NativeArg(iconPath));
        attempts.Add(bas);
        string log = "";
        bool ok = false;
        try
        {
            foreach (var a in attempts)
            {
                try
                {
                    var (code, text) = RunHidden(csc, a + " " + Quoting.NativeArg(cs));
                    log = text;
                    if (code == 0 && File.Exists(outPath)) { ok = true; break; }
                }
                catch (Exception ex) { log = Loc.F("cscStartFail", ex.Message); }
            }
        }
        finally { try { File.Delete(cs); } catch { } }
        return new(ok, log);
    }

    static (int, string) RunHidden(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        string o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, (o + err.Result).Trim());
    }

    /// <summary>Where a script's exe goes: build\&lt;same subfolders as in Scripts&gt;\Name.exe</summary>
    public static string OutPath(AppPaths paths, ScriptFile s) =>
        Path.Combine(s.Folder.Length > 0 ? Path.Combine(paths.Build, s.Folder.Replace('\\', Path.DirectorySeparatorChar)) : paths.Build, s.Name + ".exe");

    public static bool IsWritable(string path)
    {
        try { using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; } catch { return false; }
    }
}
