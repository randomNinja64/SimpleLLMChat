using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

/// <summary>
/// GUI-side named pipe client. Connects to SimpleLLMChat.Status.{cliPid} and raises events.
/// </summary>
public sealed class StatusPipeClient : IDisposable
{
    private readonly int _processId;
    private Thread _thread;
    private volatile bool _running;
    private NamedPipeClientStream _pipe;

    public event Action<int> StatusReceived;
    public event Action<IndexingStatusEvent> IndexingStatusReceived;

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
                            continue;
                        }

                        IndexingStatusEvent indexing;
                        if (StatusPipe.TryParseIndexingLine(line, out indexing))
                        {
                            Action<IndexingStatusEvent> indexingHandler = IndexingStatusReceived;
                            if (indexingHandler != null)
                                indexingHandler(indexing);
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
        IndexingStatusReceived = null;

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
