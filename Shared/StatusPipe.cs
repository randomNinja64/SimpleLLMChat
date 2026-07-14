using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

/// <summary>
/// Named-pipe status channel: CLI hosts SimpleLLMChat.Status.{pid}; GUI connects by child PID.
/// Wire format: STATUS tokens=1234
/// </summary>
public static class StatusPipe
{
    public const string PipeNamePrefix = "SimpleLLMChat.Status.";
    public const string StatusLinePrefix = "STATUS tokens=";

    public static string GetPipeName(int processId)
    {
        return PipeNamePrefix + processId.ToString(CultureInfo.InvariantCulture);
    }

    public static string FormatStatusLine(int tokens)
    {
        return StatusLinePrefix + tokens.ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryParseStatusLine(string line, out int tokens)
    {
        tokens = 0;
        if (string.IsNullOrEmpty(line))
            return false;

        line = line.Trim();
        if (!line.StartsWith(StatusLinePrefix, StringComparison.Ordinal))
            return false;

        string value = line.Substring(StatusLinePrefix.Length).Trim();
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out tokens);
    }
}

/// <summary>
/// CLI-side named pipe server. Listens on a background thread; PublishStatus is a no-op when disconnected.
/// </summary>
public sealed class StatusPipeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly object _sync = new object();
    private readonly ManualResetEvent _stop = new ManualResetEvent(false);
    private Thread _thread;
    private volatile bool _running;
    private NamedPipeServerStream _pipe;
    private StreamWriter _writer;
    private int _lastTokens;

    public StatusPipeServer()
    {
        _pipeName = StatusPipe.GetPipeName(System.Diagnostics.Process.GetCurrentProcess().Id);
    }

    public string PipeName
    {
        get { return _pipeName; }
    }

    public void Start()
    {
        if (_thread != null)
            return;

        _running = true;
        _thread = new Thread(ServerLoop);
        _thread.IsBackground = true;
        _thread.Name = "StatusPipeServer";
        _thread.Start();
    }

    public void PublishStatus(int tokens)
    {
        lock (_sync)
        {
            if (_writer != null && tokens == _lastTokens)
                return;

            _lastTokens = tokens;
            if (_writer == null)
                return;

            try
            {
                _writer.WriteLine(StatusPipe.FormatStatusLine(tokens));
            }
            catch
            {
                // Client gone — just stop writing.
                DropConnection();
            }
        }
    }

    private void ServerLoop()
    {
        try
        {
            using (var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.Out,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous))
            {
                pipe.WaitForConnection();
                if (!_running)
                    return;

                lock (_sync)
                {
                    _pipe = pipe;
                    _writer = new StreamWriter(pipe, Encoding.UTF8);
                    _writer.AutoFlush = true;

                    try
                    {
                        _writer.WriteLine(StatusPipe.FormatStatusLine(_lastTokens));
                    }
                    catch
                    {
                        DropConnection();
                    }
                }

                _stop.WaitOne();
            }
        }
        catch
        {
            // Pipe create / WaitForConnection failed or was cancelled by Dispose.
        }
        finally
        {
            lock (_sync)
            {
                DropConnection();
            }
        }
    }

    private void DropConnection()
    {
        if (_writer != null)
        {
            try { _writer.Dispose(); } catch { }
            _writer = null;
        }

        // Pipe is owned by ServerLoop's using block; just detach.
        _pipe = null;
    }

    public void Dispose()
    {
        _running = false;
        _stop.Set();

        lock (_sync)
        {
            DropConnection();
        }

        // Unblock WaitForConnection if nothing has connected yet.
        try
        {
            using (var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.In))
            {
                client.Connect(50);
            }
        }
        catch { }

        if (_thread != null && _thread.IsAlive)
        {
            try { _thread.Join(100); } catch { }
        }
        _thread = null;

        try { _stop.Close(); } catch { }
    }
}

/// <summary>
/// GUI-side named pipe client. Connects to SimpleLLMChat.Status.{cliPid} and raises StatusReceived.
/// </summary>
public sealed class StatusPipeClient : IDisposable
{
    private readonly int _processId;
    private Thread _thread;
    private volatile bool _running;
    private NamedPipeClientStream _pipe;

    public event Action<int> StatusReceived;

    public StatusPipeClient(int processId)
    {
        _processId = processId;
    }

    public void Start()
    {
        if (_thread != null)
            return;

        _running = true;
        _thread = new Thread(ClientLoop);
        _thread.IsBackground = true;
        _thread.Name = "StatusPipeClient";
        _thread.Start();
    }

    private void ClientLoop()
    {
        string pipeName = StatusPipe.GetPipeName(_processId);
        int attempt = 0;

        while (_running && attempt < 50)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In);
                pipe.Connect(200);
                _pipe = pipe;

                using (var reader = new StreamReader(pipe, Encoding.UTF8))
                {
                    while (_running)
                    {
                        string line = reader.ReadLine();
                        if (line == null)
                            break;

                        int tokens;
                        if (StatusPipe.TryParseStatusLine(line, out tokens))
                        {
                            Action<int> handler = StatusReceived;
                            if (handler != null)
                                handler(tokens);
                        }
                    }
                }

                break;
            }
            catch
            {
                attempt++;
                Thread.Sleep(100);
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

    public void Dispose()
    {
        _running = false;
        StatusReceived = null;

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
            try { thread.Join(50); } catch { }
        }
    }
}
