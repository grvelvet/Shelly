using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Shelly.Core;

/// <summary>Output kept for the history: the first MaxChars / MaxLines, then only the last lines.</summary>
public sealed class RunLog
{
    readonly object gate = new();
    readonly StringBuilder head = new();
    readonly Queue<string> tail = new();
    int tailChars, headLines;
    bool full;
    long dropped, total;
    public int MaxChars = 1048576;
    public int MaxLines = 20000;
    public int TailLines = 400;
    public bool Truncated { get { lock (gate) return full && dropped > 0; } }
    internal long TotalLines { get { lock (gate) return total; } }

    public void Add(string? s)
    {
        s ??= "";
        lock (gate)
        {
            total++;
            if (!full)
            {
                if (headLines < MaxLines && head.Length + s.Length + 2 <= MaxChars) { head.Append(s).Append("\r\n"); headLines++; return; }
                full = true;
            }
            int lim = Math.Max(1024, MaxChars / 4);
            if (s.Length > lim) s = s.Substring(0, lim) + " ...";
            tail.Enqueue(s);
            tailChars += s.Length + 2;
            while (tail.Count > 1 && (tail.Count > TailLines || tailChars > lim)) { string d = tail.Dequeue(); tailChars -= d.Length + 2; dropped++; }
        }
    }

    /// <summary>skippedFormat: the line between the first part and the last lines, {0} = number of skipped lines.</summary>
    public string GetText(string? skippedFormat)
    {
        lock (gate)
        {
            var sb = new StringBuilder(head.ToString());
            if (dropped > 0) sb.Append(string.Format(skippedFormat ?? "... {0}", dropped)).Append("\r\n");
            foreach (var t in tail) sb.Append(t).Append("\r\n");
            return sb.ToString();
        }
    }
}

/// <summary>A script run with its output captured (stdout and stderr, stderr lines start with "! ").
/// Lines collects new output for the viewer; Log keeps it for the history. Dispose (after Finished) releases the process
/// handles and the grace timer; Lines and Log stay readable.</summary>
public sealed class InlineRunner : IDisposable
{
    public readonly ConcurrentQueue<string> Lines = new();
    public readonly RunLog Log = new();
    public Process? Proc;
    public volatile bool Running;
    public volatile bool StoppedByUser;
    public int ExitCode = int.MinValue;
    public event Action<InlineRunner>? Finished;
    readonly object sync = new();
    readonly List<string> errorLines = new();
    int errorTotal;
    public const int MaxErrorLines = 20;
    readonly object gate = new();
    bool outDone, errDone, exited;
    readonly CancellationTokenSource graceCts = new();
    /// <summary>When the process has exited but a stream is still open (a child process inherited it), the run ends after this
    /// time. A normal run does not wait: it ends as soon as both streams reach their end.</summary>
    public TimeSpan StreamGrace = TimeSpan.FromSeconds(3);
    /// <summary>Set for programs whose output can come in different code pages (batch files, custom interpreters, 1.3.0):
    /// each line is read as UTF-8 when it is valid UTF-8, else in this code page (the console's OEM one). Null: UTF-8.</summary>
    public Encoding? MixedFallback;

    static readonly Regex Ansi = new(@"\x1B(\[[0-9;?]*[ -/]*[@-~]|\][^\x07\x1B]*(\x07|\x1B\\)|[@-Z\\-_])", RegexOptions.Compiled);

    /// <summary>prefix ("! " for stderr) is added after the filter, so the CLIXML noise is recognized on both streams.</summary>
    void Add(string? s, string prefix = "")
    {
        if (s == null) return;
        if (s.StartsWith("#< CLIXML") || s.StartsWith("<Objs ")) return;
        if (s.IndexOf('\x1B') >= 0) s = Ansi.Replace(s, "");
        s = prefix + s;
        lock (sync) { Log.Add(s); Lines.Enqueue(s); }
    }

    /// <summary>The first lines of the error stream (without the "! " mark) for Shelly's own log; empty when there were none.</summary>
    public string ErrorText()
    {
        lock (sync) return errorTotal > errorLines.Count ? string.Join("\n", errorLines) + "\n... +" + (errorTotal - errorLines.Count) : string.Join("\n", errorLines);
    }

    void AddError(string s)
    {
        if (s.Trim().Length == 0 || s.StartsWith("#< CLIXML") || s.StartsWith("<Objs ")) return;
        if (s.IndexOf('\x1B') >= 0) s = Ansi.Replace(s, "");
        if (s.Length > 500) s = s.Substring(0, 500) + " ...";
        lock (sync) { errorTotal++; if (errorLines.Count < MaxErrorLines) errorLines.Add(s); }
    }

    /// <summary>Header and footer lines written by Shelly.</summary>
    public void Note(string s) { lock (sync) { Log.Add(s); Lines.Enqueue(s); } }

    // The run is complete when the process has exited and both streams reached their end
    void Finish(int what)
    {
        bool fire = false;
        Process? p = null;
        lock (gate)
        {
            if (what == 0) outDone = true; else if (what == 1) errDone = true; else exited = true;
            if (!(outDone && errDone && exited) || !Running) return;
            int code = -1;
            try { code = Proc!.ExitCode; } catch { }
            ExitCode = code;
            Running = false;
            // The process object holds handles: clear and release them before notifying Finished listeners
            p = Proc;
            Proc = null;
            fire = true;
        }
        if (!fire) return;
        try { graceCts.Cancel(); } catch { }
        try { p?.Dispose(); } catch { }
        Finished?.Invoke(this);
    }

    /// <summary>Starts <paramref name="exe"/> in <paramref name="workDir"/>; <paramref name="env"/> adds environment variables
    /// (PYTHONUTF8 and the like for Python scripts); a null value removes the variable (PYTHONHOME for a venv).</summary>
    public void Start(string exe, string args, string workDir, IEnumerable<(string Key, string? Value)>? env = null)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (env != null) foreach (var (k, v) in env) { if (v == null) psi.Environment.Remove(k); else psi.Environment[k] = v; }
        Proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        Proc.OutputDataReceived += (_, e) => { if (e.Data == null) Finish(0); else Add(e.Data); };
        Proc.ErrorDataReceived += (_, e) => { if (e.Data == null) Finish(1); else { Add(e.Data, "! "); AddError(e.Data); } };
        Proc.Exited += (_, _) =>
        {
            Finish(2);
            // Streams still open after the exit (a child process inherited them): end the run after the grace time, no thread is parked meanwhile
            if (!Running) return;
            CancellationToken grace;
            try { grace = graceCts.Token; } catch (ObjectDisposedException) { return; }
            _ = Task.Delay(StreamGrace, grace).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                lock (gate) { outDone = true; errDone = true; }
                Finish(2);
            }, TaskScheduler.Default);
        };
        Running = true;
        try { Proc.Start(); }
        catch { Running = false; try { Proc.Dispose(); } catch { } Proc = null; throw; }
        Proc.StandardInput.Close();
        if (MixedFallback is { } fb)
        {
            var so = Proc.StandardOutput.BaseStream;
            var se = Proc.StandardError.BaseStream;
            new Thread(() => Pump(so, fb, false)) { IsBackground = true, Name = "shelly-out" }.Start();
            new Thread(() => Pump(se, fb, true)) { IsBackground = true, Name = "shelly-err" }.Start();
            return;
        }
        Proc.BeginOutputReadLine();
        Proc.BeginErrorReadLine();
    }

    /// <summary>Reads one stream as bytes and decodes it line by line (<see cref="ConsoleText.Decode"/>).</summary>
    void Pump(Stream s, Encoding fallback, bool err)
    {
        var split = new LineSplitter();
        var buf = new byte[8192];
        void Line(byte[] l)
        {
            string t = ConsoleText.Decode(l, fallback);
            if (err) { Add(t, "! "); AddError(t); } else Add(t);
        }
        try
        {
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
                foreach (var l in split.Push(buf, n)) Line(l);
            if (split.Flush() is { } last) Line(last);
        }
        catch { }
        finally { Finish(err ? 1 : 0); }
    }

    public void Dispose()
    {
        var p = Proc;
        Proc = null;
        try { p?.Dispose(); } catch { }
        graceCts.Dispose();
    }

    public void Stop()
    {
        var p = Proc;
        try { if (p != null && !p.HasExited) { StoppedByUser = true; p.Kill(true); } } catch { }
    }
}
