using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

/// <summary>
/// CLI-side named pipe server. Listens on a background thread; PublishLine is a no-op when disconnected.
/// Identical consecutive lines are deduped (tokens and indexing).
/// </summary>
public sealed class StatusPipeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly object _sync = new object();
    private readonly ManualResetEvent _stop = new ManualResetEvent(false);
    private Thread _thread;
    private volatile bool _running;
    private StreamWriter _writer;
    private string _lastLine = string.Empty;

    public StatusPipeServer()
    {
        _pipeName = StatusPipe.GetPipeName(System.Diagnostics.Process.GetCurrentProcess().Id);
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
        PublishLine(StatusPipe.TokensPrefix + tokens.ToString(CultureInfo.InvariantCulture));
    }

    public void PublishLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return;

        lock (_sync)
        {
            if (_writer != null && string.Equals(line, _lastLine, StringComparison.Ordinal))
                return;

            _lastLine = line;
            if (_writer == null)
                return;

            try
            {
                _writer.WriteLine(line);
            }
            catch
            {
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
                    _writer = new StreamWriter(pipe, Encoding.UTF8);
                    _writer.AutoFlush = true;

                    try
                    {
                        if (!string.IsNullOrEmpty(_lastLine))
                            _writer.WriteLine(_lastLine);
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
    }

    public void Dispose()
    {
        _running = false;
        _stop.Set();

        lock (_sync)
        {
            DropConnection();
        }

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
