using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

/// <summary>
/// Binds <see cref="HttpListener"/> on loopback without the TcpListener-then-HTTP.SYS
/// handoff. On XP that pattern throws ERROR_SHARING_VIOLATION (32):
/// "The process cannot access the file because it is being used by another process".
/// </summary>
public static class TestHttpListener
{
    private const int MinPort = 49152;
    private const int MaxPort = 65535;
    private const int MaxAttempts = 40;

    public static HttpListener StartLoopback(out string baseUrl)
    {
        Random rng = new Random(Environment.TickCount ^ Thread.CurrentThread.ManagedThreadId);
        HttpListenerException last = null;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            int port = MinPort + rng.Next(MaxPort - MinPort);
            string url = "http://127.0.0.1:" + port;
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(url + "/");
            try
            {
                listener.Start();
                baseUrl = url;
                return listener;
            }
            catch (HttpListenerException ex)
            {
                last = ex;
                TestLog.Detail("HttpListener bind failed port=" + port +
                    " NativeErrorCode=" + ex.NativeErrorCode + ": " + ex.Message);
                try { listener.Close(); } catch { }

                // Access denied is a URL-ACL problem; other ports will not help.
                if (ex.NativeErrorCode == 5)
                    break;
            }
        }

        throw new TestFailureException(
            "HttpListener bind failed after " + MaxAttempts + " attempts. Last: " +
            (last != null
                ? ("NativeErrorCode=" + last.NativeErrorCode + " " + last.Message)
                : "unknown"));
    }
}

public sealed class FakeOpenAiServer : IDisposable
{
    private sealed class ScriptedReply
    {
        public bool IsToolCall;
        public string Content;
        public string ToolName;
        public string ToolId;
        public string ToolArguments;
    }

    private readonly HttpListener _listener;
    private readonly Thread _thread;
    private readonly object _scriptGate = new object();
    private readonly Queue<ScriptedReply> _script = new Queue<ScriptedReply>();
    private volatile bool _running;
    private int _turn;

    public string BaseUrl { get; private set; }
    public string LastPath { get; private set; }
    public string LastBody { get; private set; }
    public string LastAuthorization { get; private set; }

    /// <summary>When true, chat replies include ~2KB markdown with a turn index.</summary>
    public bool LongMarkdownReplies { get; set; }

    public string FixedReply { get; set; }

    /// <summary>
    /// Delay between SSE content chunks (~20–40ms ≈ LLM-ish token cadence).
    /// 0 = still stream + flush each chunk, but with no sleep.
    /// </summary>
    public int StreamDelayMs { get; set; }

    /// <summary>Characters per SSE content delta.</summary>
    public int StreamChunkChars { get; set; }

    public FakeOpenAiServer()
    {
        FixedReply = "Hello from FakeOpenAiServer.";
        StreamDelayMs = 30;
        StreamChunkChars = 16;
        string baseUrl;
        _listener = TestHttpListener.StartLoopback(out baseUrl);
        BaseUrl = baseUrl;
        _running = true;
        _thread = new Thread(ListenLoop) { IsBackground = true };
        _thread.Start();
        TestLog.Detail("FakeOpenAiServer listening at " + BaseUrl);
    }

    public void ClearScript()
    {
        lock (_scriptGate)
            _script.Clear();
    }

    public void EnqueueContent(string content)
    {
        lock (_scriptGate)
        {
            _script.Enqueue(new ScriptedReply
            {
                IsToolCall = false,
                Content = content ?? ""
            });
        }
    }

    public void EnqueueToolCall(string name, string id, string argumentsJson)
    {
        lock (_scriptGate)
        {
            _script.Enqueue(new ScriptedReply
            {
                IsToolCall = true,
                ToolName = name ?? "",
                ToolId = string.IsNullOrEmpty(id) ? "call_1" : id,
                ToolArguments = argumentsJson ?? "{}"
            });
        }
    }

    private void ListenLoop()
    {
        while (_running)
        {
            HttpListenerContext ctx = null;
            try
            {
                ctx = _listener.GetContext();
            }
            catch
            {
                if (!_running) break;
                continue;
            }

            try
            {
                Handle(ctx);
            }
            catch (Exception ex)
            {
                try
                {
                    byte[] err = Encoding.UTF8.GetBytes("{\"error\":\"" + ex.Message.Replace("\"", "'") + "\"}");
                    ctx.Response.StatusCode = 500;
                    ctx.Response.OutputStream.Write(err, 0, err.Length);
                    ctx.Response.Close();
                }
                catch { }
            }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        string path = ctx.Request.Url.AbsolutePath ?? "";
        LastPath = path;
        LastAuthorization = ctx.Request.Headers["Authorization"];
        string body = "";
        if (ctx.Request.HasEntityBody)
        {
            using (StreamReader reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = reader.ReadToEnd();
        }
        LastBody = body;
        TestLog.Detail("FakeOpenAi " + ctx.Request.HttpMethod + " " + path + " body_len=" + body.Length);

        if (path.EndsWith("/v1/models", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/v1/models", StringComparison.OrdinalIgnoreCase))
        {
            string json = "{\"object\":\"list\",\"data\":[{\"id\":\"test-model\",\"object\":\"model\"}]}";
            WriteJson(ctx, 200, json);
            return;
        }

        if (path.IndexOf("/v1/chat/completions", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            WriteSseChat(ctx);
            return;
        }

        WriteJson(ctx, 404, "{\"error\":\"not found\"}");
    }

    private void WriteSseChat(HttpListenerContext ctx)
    {
        _turn++;
        ScriptedReply scripted = null;
        lock (_scriptGate)
        {
            if (_script.Count > 0)
                scripted = _script.Dequeue();
        }

        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.SendChunked = true;

        using (Stream output = ctx.Response.OutputStream)
        {
            if (scripted != null && scripted.IsToolCall)
                WriteToolCallSse(output, scripted);
            else
                WriteContentSse(output, ResolveContent(scripted));
        }
        ctx.Response.Close();
    }

    private string ResolveContent(ScriptedReply scripted)
    {
        if (scripted != null)
            return scripted.Content ?? "";

        if (LongMarkdownReplies)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Reply turn " + _turn);
            sb.AppendLine();
            sb.AppendLine("This is a canned markdown reply for long-chat profiling.");
            sb.AppendLine();
            sb.AppendLine("- item one");
            sb.AppendLine("- item two");
            sb.AppendLine("- item three");
            sb.AppendLine();
            sb.AppendLine("```csharp");
            for (int i = 0; i < 40; i++)
                sb.AppendLine("// line " + i + " padding for ~2KB body turn=" + _turn);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("End of turn " + _turn + ".");
            return sb.ToString();
        }

        return FixedReply ?? "ok";
    }

    private void WriteContentSse(Stream output, string content)
    {
        int delayMs = StreamDelayMs;
        int chunkChars = StreamChunkChars > 0 ? StreamChunkChars : 16;
        if (content == null)
            content = "";

        for (int i = 0; i < content.Length; i += chunkChars)
        {
            if (!_running)
                break;

            int len = Math.Min(chunkChars, content.Length - i);
            string piece = content.Substring(i, len);
            string evt = "data: {\"choices\":[{\"delta\":{\"content\":\"" +
                EscapeJson(piece) + "\"}}]}\n\n";
            byte[] bytes = Encoding.UTF8.GetBytes(evt);
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
            if (delayMs > 0)
                Thread.Sleep(delayMs);
        }

        byte[] trail = Encoding.UTF8.GetBytes(
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n");
        output.Write(trail, 0, trail.Length);
        output.Flush();
    }

    private void WriteToolCallSse(Stream output, ScriptedReply call)
    {
        // Name announcement (matches SseStreamParser: name with empty args first).
        string nameEvt = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"" +
            EscapeJson(call.ToolId) + "\",\"function\":{\"name\":\"" +
            EscapeJson(call.ToolName) + "\",\"arguments\":\"\"}}]}}]}\n\n";
        byte[] nameBytes = Encoding.UTF8.GetBytes(nameEvt);
        output.Write(nameBytes, 0, nameBytes.Length);
        output.Flush();

        string args = call.ToolArguments ?? "{}";
        int chunkChars = StreamChunkChars > 0 ? StreamChunkChars : 16;
        for (int i = 0; i < args.Length; i += chunkChars)
        {
            if (!_running)
                break;
            int len = Math.Min(chunkChars, args.Length - i);
            string piece = args.Substring(i, len);
            string argsEvt = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"" +
                EscapeJson(piece) + "\"}}]}}]}\n\n";
            byte[] argsBytes = Encoding.UTF8.GetBytes(argsEvt);
            output.Write(argsBytes, 0, argsBytes.Length);
            output.Flush();
        }

        byte[] trail = Encoding.UTF8.GetBytes(
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n\n" +
            "data: [DONE]\n\n");
        output.Write(trail, 0, trail.Length);
        output.Flush();
    }

    private static string EscapeJson(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }

    private static void WriteJson(HttpListenerContext ctx, int status, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        _running = false;
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try
        {
            if (!_thread.Join(2000))
                TestLog.Detail("FakeOpenAiServer thread did not exit promptly");
        }
        catch { }
    }
}
