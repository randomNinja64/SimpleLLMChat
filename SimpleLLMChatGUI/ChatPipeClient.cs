using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

/// <summary>
/// GUI-side named pipe client. Connects to SimpleLLMChat.Chat.{cliPid} and raises events.
/// </summary>
public sealed class ChatPipeClient : IDisposable
{
    private readonly int _processId;
    private Thread _thread;
    private volatile bool _running;
    private NamedPipeClientStream _pipe;

    public event Action<int> StatusReceived;
    public event Action<IndexingStatusEvent> IndexingStatusReceived;
    public event Action ReadyReceived;
    public event Action<string, string, string> ApprovalReceived;
    public event Action<string> TextReceived;
    public event Action<ChatBlockKind, string> BlockStarted;
    public event Action BlockEnded;

    public ChatPipeClient(int processId)
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
        _thread.Name = "ChatPipeClient";
        _thread.Start();
    }

    private void ClientLoop()
    {
        string pipeName = ChatPipe.GetPipeName(_processId);
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

                        string text;
                        if (ChatPipe.TryParseTextLine(line, out text))
                        {
                            Action<string> handler = TextReceived;
                            if (handler != null)
                                handler(text);
                            continue;
                        }

                        ChatBlockKind blockKind;
                        string blockName;
                        if (ChatPipe.TryParseBlockLine(line, out blockKind, out blockName))
                        {
                            Action<ChatBlockKind, string> handler = BlockStarted;
                            if (handler != null)
                                handler(blockKind, blockName);
                            continue;
                        }

                        if (ChatPipe.TryParseEndBlockLine(line))
                        {
                            Action handler = BlockEnded;
                            if (handler != null)
                                handler();
                            continue;
                        }

                        if (ChatPipe.TryParseReadyLine(line))
                        {
                            Action handler = ReadyReceived;
                            if (handler != null)
                                handler();
                            continue;
                        }

                        string approvalTool;
                        string approvalArgs;
                        string approvalConfidence;
                        if (ChatPipe.TryParseApprovalLine(line, out approvalTool, out approvalArgs, out approvalConfidence))
                        {
                            Action<string, string, string> handler = ApprovalReceived;
                            if (handler != null)
                                handler(approvalTool, approvalArgs, approvalConfidence);
                            continue;
                        }

                        int tokens;
                        if (ChatPipe.TryParseStatusLine(line, out tokens))
                        {
                            Action<int> handler = StatusReceived;
                            if (handler != null)
                                handler(tokens);
                            continue;
                        }

                        IndexingStatusEvent indexing;
                        if (ChatPipe.TryParseIndexingLine(line, out indexing))
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
        ReadyReceived = null;
        ApprovalReceived = null;
        TextReceived = null;
        BlockStarted = null;
        BlockEnded = null;

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
