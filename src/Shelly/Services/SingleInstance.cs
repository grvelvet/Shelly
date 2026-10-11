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

    public SingleInstance(string root, bool waitForPrevious = false)
    {
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToLowerInvariant() + "|" + Environment.UserName))).Substring(0, 16);
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
        try { mutex.ReleaseMutex(); } catch { }
        mutex.Dispose();
    }
}
