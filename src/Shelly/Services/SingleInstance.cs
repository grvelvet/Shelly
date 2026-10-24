using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Shelly.Services;

/// <summary>One Shelly per folder (portable copies in other folders run side by side). A second start sends
/// "show" or "run\t&lt;rel&gt;" to the first one through a named pipe of the current user.</summary>
public sealed class SingleInstance : IDisposable
{
    readonly Mutex mutex;
    readonly string pipeName;
    readonly CancellationTokenSource cts = new();
    public bool IsFirst { get; }
    public event Action<string>? MessageReceived;

    static string IdFor(string root) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToLowerInvariant() + "|" + Environment.UserName))).Substring(0, 16);

    /// <summary>Is the Shelly of this folder running (1.2.0: an unattended run asks before it starts on its own)?</summary>
    public static bool IsRunning(string root)
    {
        try
        {
            if (!Mutex.TryOpenExisting(@"Local\Shelly-" + IdFor(root), out var m)) return false;
            m.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException) { return true; }
        catch { return false; }
    }

    /// <summary>Sends a message to the running Shelly of this folder; false = nobody took it.</summary>
    public static bool SendTo(string root, string message, int timeoutMs = 3000)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", "Shelly-" + IdFor(root), PipeDirection.Out);
            c.Connect(timeoutMs);
            var b = Encoding.UTF8.GetBytes(message);
            c.Write(b, 0, b.Length);
            c.Flush();
            return true;
        }
        catch { return false; }
    }

    public SingleInstance(string root, bool waitForPrevious = false)
    {
        string id = IdFor(root);
        mutex = new Mutex(true, @"Local\Shelly-" + id, out bool created);
        if (!created && waitForPrevious)
        {
            // Restart: the previous Shelly is closing
            try { created = mutex.WaitOne(10000); } catch (AbandonedMutexException) { created = true; }
        }
        IsFirst = created;
        pipeName = "Shelly-" + id;
    }

    public void Send(string message)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            c.Connect(3000);
            var b = Encoding.UTF8.GetBytes(message);
            c.Write(b, 0, b.Length);
            c.Flush();
        }
        catch { }
    }

    public void Listen()
    {
        var t = new Thread(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var s = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    s.WaitForConnectionAsync(cts.Token).GetAwaiter().GetResult();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    string msg = Encoding.UTF8.GetString(ms.ToArray());
                    if (msg.Length > 0 && msg.Length < 4096) MessageReceived?.Invoke(msg);
                }
                catch (OperationCanceledException) { break; }
                catch { Thread.Sleep(200); }
            }
        }) { IsBackground = true, Name = "Shelly pipe" };
        t.Start();
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();   // the listener stops on the cancellation; a token read after this is caught in its loop
        try { mutex.ReleaseMutex(); } catch { }
        mutex.Dispose();
    }
}
