using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

/// <summary>
/// Connects to SimpleLLMChat.Chat.{pid} and waits for control-plane lines.
/// </summary>
public sealed class ChatPipeProbe : IDisposable
{
    private readonly int _processId;
    private Thread _thread;
    private volatile bool _running;
    private NamedPipeClientStream _pipe;
    private readonly object _gate = new object();
    private readonly System.Collections.Generic.List<string> _lines =
        new System.Collections.Generic.List<string>();

    public ChatPipeProbe(int processId)
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
            Name = "ChatPipeProbe"
        };
        _thread.Start();
    }

    private void ClientLoop()
    {
        string pipeName = ChatPipe.GetPipeName(_processId);
        int attempt = 0;

        while (_running && attempt < 100)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In);
                pipe.Connect(200);
                _pipe = pipe;
                TestLog.Detail("ChatPipeProbe connected: " + pipeName);

                using (var reader = new StreamReader(pipe, Encoding.UTF8))
                {
                    while (_running)
                    {
                        string line = reader.ReadLine();
                        if (line == null)
                            break;
                        lock (_gate)
                            _lines.Add(line);
                        TestLog.Detail("ChatPipeProbe: " + line);
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

    public int LastTokens()
    {
        int tokens = 0;
        lock (_gate)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                int parsed;
                if (ChatPipe.TryParseStatusLine(_lines[i], out parsed))
                    tokens = parsed;
            }
        }
        return tokens;
    }

    /// <summary>All chat text events received so far, concatenated.</summary>
    public string ChatText()
    {
        StringBuilder sb = new StringBuilder();
        lock (_gate)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                string text;
                if (ChatPipe.TryParseTextLine(_lines[i], out text))
                    sb.Append(text);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Chat events in order, one per line: "text ...", "block Kind name", "endblock".
    /// Consecutive text events are joined.
    /// </summary>
    public string ChatEvents()
    {
        StringBuilder sb = new StringBuilder();
        bool inText = false;
        lock (_gate)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                string text, name;
                ChatBlockKind kind;
                if (ChatPipe.TryParseTextLine(_lines[i], out text))
                {
                    sb.Append(inText ? "" : "\ntext ").Append(text.Replace("\n", "\\n"));
                    inText = true;
                }
                else if (ChatPipe.TryParseBlockLine(_lines[i], out kind, out name))
                {
                    sb.Append("\nblock " + kind + " " + name);
                    inText = false;
                }
                else if (ChatPipe.TryParseEndBlockLine(_lines[i]))
                {
                    sb.Append("\nendblock");
                    inText = false;
                }
            }
        }
        return sb.Append('\n').ToString();
    }

    /// <summary>Paths from every session (auto-save) line so far, in order.</summary>
    public System.Collections.Generic.List<string> Sessions()
    {
        var paths = new System.Collections.Generic.List<string>();
        lock (_gate)
        {
            foreach (string line in _lines)
            {
                string path;
                if (ChatPipe.TryParseSessionLine(line, out path))
                    paths.Add(path);
            }
        }
        return paths;
    }

    public bool WaitForSessions(int count, int timeoutMs)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (Sessions().Count >= count)
                return true;
            Thread.Sleep(25);
        }
        return false;
    }

    /// <summary>The workspace named by the latest ready line, or null.</summary>
    public string LastWorkspace()
    {
        string last = null;
        lock (_gate)
        {
            foreach (string line in _lines)
            {
                string workspace;
                if (ChatPipe.TryParseReadyLine(line, out workspace))
                    last = workspace;
            }
        }
        return last;
    }

    /// <summary>Waits until the latest ready line names <paramref name="workspace"/>.</summary>
    public bool WaitForWorkspace(string workspace, int timeoutMs)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (string.Equals(LastWorkspace(), workspace, StringComparison.OrdinalIgnoreCase))
                return true;
            Thread.Sleep(25);
        }
        return false;
    }

    public bool WaitForReady(int timeoutMs)
    {
        return WaitFor(line => ChatPipe.TryParseReadyLine(line), timeoutMs);
    }

    public bool WaitForApproval(int timeoutMs, out string toolName, out string arguments, out string confidence)
    {
        string name = null;
        string args = null;
        string conf = null;
        bool ok = WaitFor(line =>
        {
            string n, a, c;
            if (!ChatPipe.TryParseApprovalLine(line, out n, out a, out c))
                return false;
            name = n;
            args = a;
            conf = c;
            return true;
        }, timeoutMs);
        toolName = name;
        arguments = args;
        confidence = conf;
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
