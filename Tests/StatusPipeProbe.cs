using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

/// <summary>
/// Connects to SimpleLLMChat.Status.{pid} and waits for control-plane lines.
/// </summary>
public sealed class StatusPipeProbe : IDisposable
{
    private readonly int _processId;
    private Thread _thread;
    private volatile bool _running;
    private NamedPipeClientStream _pipe;
    private readonly object _gate = new object();
    private readonly System.Collections.Generic.List<string> _lines =
        new System.Collections.Generic.List<string>();

    public StatusPipeProbe(int processId)
    {
        _processId = processId;
    }

    public void Start()
    {
        if (_thread != null)
            return;

        _running = true;
        _thread = new Thread(ClientLoop)
        {
            IsBackground = true,
            Name = "StatusPipeProbe"
        };
        _thread.Start();
    }

    private void ClientLoop()
    {
        string pipeName = StatusPipe.GetPipeName(_processId);
        int attempt = 0;

        while (_running && attempt < 100)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In);
                pipe.Connect(200);
                _pipe = pipe;
                TestLog.Detail("StatusPipeProbe connected: " + pipeName);

                using (var reader = new StreamReader(pipe, Encoding.UTF8))
                {
                    while (_running)
                    {
                        string line = reader.ReadLine();
                        if (line == null)
                            break;
                        lock (_gate)
                            _lines.Add(line);
                        TestLog.Detail("StatusPipeProbe: " + line);
                    }
                }
                break;
            }
            catch
            {
                attempt++;
                Thread.Sleep(50);
            }
            finally
            {
                if (_pipe != null)
                {
                    try { _pipe.Dispose(); } catch { }
                    _pipe = null;
                }
            }
        }
    }

    public bool WaitForReady(int timeoutMs)
    {
        return WaitFor(line => StatusPipe.TryParseReadyLine(line), timeoutMs);
    }

    public bool WaitForApproval(int timeoutMs, out string toolName, out string arguments)
    {
        string name = null;
        string args = null;
        bool ok = WaitFor(line =>
        {
            string n, a;
            if (!StatusPipe.TryParseApprovalLine(line, out n, out a))
                return false;
            name = n;
            args = a;
            return true;
        }, timeoutMs);
        toolName = name;
        arguments = args;
        return ok;
    }

    private bool WaitFor(Func<string, bool> predicate, int timeoutMs)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        int scanned = 0;
        while (DateTime.UtcNow < deadline)
        {
            lock (_gate)
            {
                for (; scanned < _lines.Count; scanned++)
                {
                    if (predicate(_lines[scanned]))
                        return true;
                }
            }
            Thread.Sleep(25);
        }
        return false;
    }

    public void Dispose()
    {
        _running = false;
        NamedPipeClientStream pipe = _pipe;
        _pipe = null;
        if (pipe != null)
        {
            try { pipe.Dispose(); } catch { }
        }

        Thread thread = _thread;
        _thread = null;
        if (thread != null && thread.IsAlive)
        {
            try { thread.Join(200); } catch { }
        }
    }
}
