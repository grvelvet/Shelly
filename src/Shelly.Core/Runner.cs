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
    public long TotalLines { get { lock (gate) return total; } }

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
/// Lines collects new output for the viewer; Log keeps it for the history.</summary>
public sealed class InlineRunner
{
    public readonly ConcurrentQueue<string> Lines = new();
    public readonly RunLog Log = new();
    public string Id = "";
    public Process? Proc;
    public volatile bool Running;
    public volatile bool StoppedByUser;
    public int ExitCode = int.MinValue;
    public event Action<InlineRunner>? Finished;
    readonly object sync = new();
    readonly object gate = new();
    bool outDone, errDone, exited;

    static readonly Regex Ansi = new(@"\x1B(\[[0-9;?]*[ -/]*[@-~]|\][^\x07\x1B]*(\x07|\x1B\\)|[@-Z\\-_])", RegexOptions.Compiled);

    void Add(string? s)
    {
        if (s == null) return;
        if (s.StartsWith("#< CLIXML") || s.StartsWith("<Objs ")) return;
        if (s.IndexOf('\x1B') >= 0) s = Ansi.Replace(s, "");
        lock (sync) { Log.Add(s); Lines.Enqueue(s); }
    }

    /// <summary>Header and footer lines written by Shelly.</summary>
    public void Note(string s) { lock (sync) { Log.Add(s); Lines.Enqueue(s); } }

    /// <summary>Text so far for a viewer that starts showing this run; the queued lines are dropped (they are in the text).</summary>
    public string Snapshot(string skippedFormat)
    {
        lock (sync) { while (Lines.TryDequeue(out _)) { } return Log.GetText(skippedFormat); }
    }

    // The run is complete when the process has exited and both streams reached their end
    void Finish(int what)
    {
        bool fire = false;
        lock (gate)
        {
            if (what == 0) outDone = true; else if (what == 1) errDone = true; else exited = true;
            if (!(outDone && errDone && exited) || !Running) return;
            int code = -1;
            try { code = Proc!.ExitCode; } catch { }
            ExitCode = code;
            Running = false;
            fire = true;
        }
        if (fire) Finished?.Invoke(this);
    }

    public void Start(string exe, string args, string workDir)
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
        Proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        Proc.OutputDataReceived += (_, e) => { if (e.Data == null) Finish(0); else Add(e.Data); };
        Proc.ErrorDataReceived += (_, e) => { if (e.Data == null) Finish(1); else Add("! " + e.Data); };
        Proc.Exited += (_, _) =>
        {
            // A child process can keep the streams open: then the run ends 3 seconds after the exit
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(3000);
                lock (gate) { outDone = true; errDone = true; }
                Finish(2);
            });
            Finish(2);
        };
        Running = true;
        try { Proc.Start(); }
        catch { Running = false; throw; }
        Proc.StandardInput.Close();
        Proc.BeginOutputReadLine();
        Proc.BeginErrorReadLine();
    }

    public void Stop()
    {
        try { if (Proc != null && !Proc.HasExited) { StoppedByUser = true; Proc.Kill(true); } } catch { }
    }
}
