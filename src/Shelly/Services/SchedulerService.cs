using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text;
using Shelly.Core;

namespace Shelly.Services;

/// <summary>Schedules → Windows Task Scheduler (1.2.0), through schtasks.exe and a task XML file: no extra package, no COM
/// interop assembly, nothing that needs administrator rights. Every task is in the \Shelly\ folder, runs as the current
/// user only while signed in (InteractiveToken, least privilege) and starts "Shelly.exe --scheduled &lt;id&gt;"; what the id
/// means (script, preset, how to run) is read from the settings, which stay the source of truth. The tasks are checked
/// against the settings in the background at startup.</summary>
public sealed class SchedulerService
{
    readonly AppState st;
    public string Instance { get; }
    public string ExePath { get; }
    /// <summary>The last check (null: not checked yet, or schtasks failed: see <see cref="QueryError"/>).</summary>
    public Reconciliation? Last { get; private set; }
    public string? QueryError { get; private set; }
    public bool Checking { get; private set; }
    public event Action? Changed;

    public SchedulerService(AppState st)
    {
        this.st = st;
        Instance = SchTasks.InstanceId(st.Paths.Root, Environment.UserName);
        ExePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Shelly.exe");
    }

    /// <summary>ok | missing | outdated | "" (not known).</summary>
    public string StateOf(string id) => Last != null && Last.State.TryGetValue(id, out var v) ? v : "";

    static bool encodings;

    static (int Code, string Text) Exec(IEnumerable<string> args, int timeoutMs = 30000)
    {
        if (!encodings) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); encodings = true; }
        Encoding oem;
        try { oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch { oem = Encoding.Default; }
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = oem, StandardErrorEncoding = oem
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return (-1, "schtasks.exe: timeout"); }
            p.WaitForExit();
            return (p.ExitCode, (o.Result + "\n" + e.Result).Trim());
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    /// <summary>The first meaningful line of schtasks' answer (its errors start with "ERROR:" in the system language).</summary>
    static string Short(string text)
    {
        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length > 300 ? line.Substring(0, 300) + "…" : line;
    }

    static string UserSid()
    {
        try { return WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserDomainName + "\\" + Environment.UserName; }
        catch { return Environment.UserDomainName + "\\" + Environment.UserName; }
    }

    /// <summary>Creates or replaces the task of a schedule. null = done, else schtasks' error. Runs in the background:
    /// it does not touch <see cref="Last"/>, which is read and changed on the calling (UI) thread only (1.3.1).</summary>
    static string? Apply(Schedule s, string instance, string exePath)
    {
        string name = SchTasks.TaskName(instance, s.Id);
        string xml = TaskXml.Build(s, exePath, UserSid(), name, "Shelly: " + s.Rel + " · " + s.Describe(), DateTime.Now);
        string tmp = Path.Combine(Path.GetTempPath(), "shelly-task-" + s.Id + "-" + Environment.ProcessId + ".xml");
        try
        {
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            var (code, text) = Exec(SchTasks.CreateArgs(name, tmp));
            AppLog.Write("Task " + name + (code == 0 ? " saved" : " not saved: " + text));
            return code != 0 ? Short(text) : null;
        }
        catch (Exception ex) { return ex.Message; }
        finally { try { File.Delete(tmp); } catch { } }
    }

    public async Task<string?> ApplyAsync(Schedule s)
    {
        string instance = Instance, exe = ExePath;
        string? fail = await Task.Run(() => Apply(s, instance, exe));
        if (fail == null && Last != null) Last.State[s.Id] = "ok";
        return fail;
    }

    /// <summary>Deletes the task of a schedule id. null = done (or there was none to delete, as far as the last check knows).</summary>
    public async Task<string?> DeleteAsync(string id)
    {
        string name = SchTasks.TaskName(Instance, id);
        var (code, text) = await Task.Run(() => Exec(SchTasks.DeleteArgs(name)));
        AppLog.Write("Task " + name + (code == 0 ? " deleted" : " not deleted: " + text));
        bool known = StateOf(id) is "ok" or "outdated" or "";
        Last?.State.Remove(id);
        return code == 0 || !known ? null : Short(text);
    }

    /// <summary>Tasks of this Shelly that no schedule uses; returns how many went.</summary>
    public async Task<int> CleanOrphansAsync()
    {
        var names = Last?.Orphans.ToList() ?? new();
        int n = await Task.Run(() => names.Count(name => Exec(SchTasks.DeleteArgs(name)).Code == 0));
        await RefreshAsync();
        return n;
    }

    /// <summary>Reads all tasks (schtasks /Query, in the background) and compares them with the schedules in the settings.</summary>
    public async Task RefreshAsync()
    {
        if (Checking) return;
        Checking = true;
        Changed?.Invoke();
        var schedules = ScheduleList.All(st.Settings);
        try
        {
            var (code, text) = await Task.Run(() => Exec(SchTasks.QueryArgs(), 90000));
            if (code != 0)
            {
                QueryError = Short(text);
                Last = null;
                AppLog.Write("Task Scheduler could not be read: " + text);
            }
            else
            {
                QueryError = null;
                Last = SchTasks.Reconcile(schedules, SchTasks.ParseQuery(text), Instance, ExePath);
                if (Last.Missing > 0 || Last.Orphans.Count > 0)
                    AppLog.Write($"Task Scheduler: {Last.Missing} schedule(s) without a valid task, {Last.Orphans.Count} leftover task(s)");
            }
        }
        finally
        {
            Checking = false;
            Changed?.Invoke();
        }
    }

    /// <summary>How many things in Settings > Schedules need the user (tasks missing or outdated, leftover tasks).</summary>
    public int Attention => Last == null ? 0 : Last.Missing + Last.Orphans.Count;
}
