using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

/// <summary>
/// Shared test harness utilities for SimpleLLMChat suite EXEs (net40).
/// </summary>
public static class TestLog
{
    public const long SlowThresholdMs = 5000;

    private static StreamWriter _writer;
    private static StreamWriter _perfWriter;
    private static string _logPath;
    private static string _perfPath;
    private static string _suiteName = "Suite";
    private static readonly object _gate = new object();
    private static readonly List<PerfRow> _rows = new List<PerfRow>();

    private sealed class PerfRow
    {
        public string Case;
        public string Status;
        public long CaseMs;
        public long ProcessMs;
    }

    public static string LogPath { get { return _logPath; } }
    public static string PerfPath { get { return _perfPath; } }

    public static void Open(string suiteName, string logPath)
    {
        _suiteName = suiteName ?? "Suite";
        if (string.IsNullOrEmpty(logPath))
        {
            string logsDir = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(
                    AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/')))) ?? ".",
                "logs");
            Directory.CreateDirectory(logsDir);
            logPath = Path.Combine(logsDir, _suiteName + "-local.log");
        }

        string dir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _logPath = Path.GetFullPath(logPath);
        _perfPath = Path.ChangeExtension(_logPath, ".perf.tsv");
        if (_perfPath.EndsWith(".log.perf.tsv", StringComparison.OrdinalIgnoreCase))
            _perfPath = _logPath.Substring(0, _logPath.Length - 4) + ".perf.tsv";
        // Prefer Suite.perf.tsv beside Suite.log
        string baseName = Path.GetFileNameWithoutExtension(_logPath);
        _perfPath = Path.Combine(Path.GetDirectoryName(_logPath) ?? ".", baseName + ".perf.tsv");

        _writer = new StreamWriter(_logPath, false, Encoding.UTF8) { AutoFlush = true };
        _perfWriter = new StreamWriter(_perfPath, false, Encoding.UTF8) { AutoFlush = true };
        _perfWriter.WriteLine("case\tstatus\tcase_ms\tprocess_ms");
        _rows.Clear();

        string header = "Log: " + _logPath;
        Console.WriteLine(header);
        _writer.WriteLine(header);
        Info("START " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Info("Suite=" + _suiteName);
        Info("Machine=" + Environment.MachineName + " OS=" + Environment.OSVersion);
        Info("BaseDirectory=" + AppDomain.CurrentDomain.BaseDirectory);
        Info("WorkingDirectory=" + Environment.CurrentDirectory);
        Info("Perf=" + _perfPath);
    }

    public static void Close(int pass, int fail, int skip, long suiteMs)
    {
        WriteHotSpots(10);
        Info(string.Format("DONE pass={0} fail={1} skip={2} suite_ms={3}", pass, fail, skip, suiteMs));
        if (_writer != null)
        {
            _writer.Dispose();
            _writer = null;
        }
        if (_perfWriter != null)
        {
            _perfWriter.Dispose();
            _perfWriter = null;
        }
    }

    public static void Info(string message)
    {
        string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message;
        lock (_gate)
        {
            Console.WriteLine(line);
            if (_writer != null)
                _writer.WriteLine(line);
        }
    }

    public static void Detail(string message)
    {
        lock (_gate)
        {
            if (_writer != null)
                _writer.WriteLine("  " + message);
        }
    }

    /// <summary>
    /// Tool screenshots put tens of KB of JPEG base64 in stdout. Keep a short prefix in the
    /// log so the file stays readable; callers still parse the full payload.
    /// </summary>
    public static string FormatStdoutForLog(string stdout)
    {
        if (string.IsNullOrEmpty(stdout))
            return stdout ?? "";

        string trimmed = stdout.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return stdout;

        try
        {
            JObject obj = JObject.Parse(trimmed);
            JObject image = obj["image"] as JObject;
            if (image == null)
                return stdout;

            string data = image["data"] != null && image["data"].Type == JTokenType.String
                ? image["data"].Value<string>()
                : null;
            if (string.IsNullOrEmpty(data) || data.Length <= 48)
                return stdout;

            image["data"] = data.Substring(0, 24) + "... (" + data.Length + " chars)";
            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }
        catch
        {
            return stdout;
        }
    }

    public static void Result(string caseName, string status, long caseMs, long processMs, string detail)
    {
        bool slow = caseMs >= SlowThresholdMs;
        string tag = status;
        if (slow && (status == "PASS" || status == "FAIL"))
            tag = status + "+SLOW";

        string console = string.Format("[{0}] {1}  (case_ms={2} process_ms={3})",
            tag, caseName, caseMs, processMs);
        Console.WriteLine(console);
        if (slow)
            Console.WriteLine("       [SLOW] case_ms >= " + SlowThresholdMs);

        lock (_gate)
        {
            if (_writer != null)
            {
                _writer.WriteLine(console);
                if (!string.IsNullOrEmpty(detail))
                    _writer.WriteLine(detail);
            }
            if (_perfWriter != null)
                _perfWriter.WriteLine(string.Format("{0}\t{1}\t{2}\t{3}", caseName, status, caseMs, processMs));

            _rows.Add(new PerfRow
            {
                Case = caseName,
                Status = status,
                CaseMs = caseMs,
                ProcessMs = processMs
            });
        }
    }

    private static void WriteHotSpots(int topN)
    {
        List<PerfRow> sorted = new List<PerfRow>(_rows);
        sorted.Sort((a, b) =>
        {
            int cmp = b.ProcessMs.CompareTo(a.ProcessMs);
            if (cmp != 0) return cmp;
            return b.CaseMs.CompareTo(a.CaseMs);
        });

        Info("Hot spots (top " + topN + " by process_ms):");
        int n = Math.Min(topN, sorted.Count);
        for (int i = 0; i < n; i++)
        {
            PerfRow r = sorted[i];
            Info(string.Format("  {0}. {1} status={2} process_ms={3} case_ms={4}",
                i + 1, r.Case, r.Status, r.ProcessMs, r.CaseMs));
        }
    }
}

public class TestFailureException : Exception
{
    public TestFailureException(string message) : base(message) { }
}

public static class TestAssert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new TestFailureException(message ?? "Expected true.");
    }

    public static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new TestFailureException(
                (message ?? "Equal") + "\n  expected: " + (expected ?? "(null)") +
                "\n  actual:   " + (actual ?? "(null)"));
        }
    }

    public static void Equal(int expected, int actual, string message)
    {
        if (expected != actual)
        {
            throw new TestFailureException(
                (message ?? "equal") + " expected=" + expected + " actual=" + actual);
        }
    }

    public static void Contains(string haystack, string needle, string message)
    {
        if (haystack == null || needle == null || haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
        {
            throw new TestFailureException(
                (message ?? "contains") + "\n  needle: " + (needle ?? "(null)") +
                "\n  haystack: " + Truncate(haystack, 500));
        }
    }

    public static void ContainsIgnoreCase(string haystack, string needle, string message)
    {
        if (haystack == null || needle == null ||
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new TestFailureException(
                (message ?? "contains") + "\n  needle: " + (needle ?? "(null)") +
                "\n  haystack: " + Truncate(haystack, 500));
        }
    }

    private static string Truncate(string s, int max)
    {
        if (s == null) return "(null)";
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "...";
    }
}

public static class TestRunner
{
    [ThreadStatic]
    private static long _currentProcessMs;

    public static int PassCount;
    public static int FailCount;
    public static int SkipCount;

    public static void ResetCounts()
    {
        PassCount = 0;
        FailCount = 0;
        SkipCount = 0;
    }

    public static void AddProcessMs(long ms)
    {
        if (ms > 0)
            _currentProcessMs += ms;
    }

    public static void Run(string name, Action action)
    {
        _currentProcessMs = 0;
        Stopwatch sw = Stopwatch.StartNew();
        string detail = null;
        string status;
        try
        {
            action();
            status = "PASS";
            PassCount++;
        }
        catch (TestSkipException ex)
        {
            status = "SKIP";
            SkipCount++;
            detail = "  reason: " + ex.Message;
        }
        catch (Exception ex)
        {
            status = "FAIL";
            FailCount++;
            detail = "  " + ex.GetType().Name + ": " + ex.Message;
            if (ex.InnerException != null)
                detail += "\n  inner: " + ex.InnerException.Message;
        }
        sw.Stop();
        TestLog.Result(name, status, sw.ElapsedMilliseconds, _currentProcessMs, detail);
    }

    public static void Skip(string reason)
    {
        throw new TestSkipException(reason);
    }

    public static int ExitCode
    {
        get { return FailCount > 0 ? 1 : 0; }
    }
}

public class TestSkipException : Exception
{
    public TestSkipException(string message) : base(message) { }
}

public sealed class ToolInvokeResult
{
    public int ExitCode;
    public string Stdout;
    public string Stderr;
    public string Text;
    public string ImageBase64;
    public string ImageMime;
    public long ElapsedMs;
    public bool TimedOut;
}

public static class ToolClient
{
    private const int PipeDrainTimeoutMs = 500;
    private const int PipeCloseJoinTimeoutMs = 100;

    public static string ProductExe(string fileName)
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
    }

    public static bool ProductExists(string fileName)
    {
        return File.Exists(ProductExe(fileName));
    }

    public static ToolInvokeResult Invoke(
        string exePath,
        string toolName,
        JObject arguments,
        JObject config = null,
        int timeoutMs = 0)
    {
        if (!File.Exists(exePath))
            throw new TestFailureException("Product EXE not found: " + exePath);

        JObject root = new JObject();
        root["config"] = config ?? new JObject();
        root["arguments"] = arguments ?? new JObject();
        string stdin = root.ToString(Newtonsoft.Json.Formatting.None);

        TestLog.Detail("Invoke: " + exePath + " " + toolName);
        TestLog.Detail("stdin: " + stdin);

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = toolName ?? "",
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? ".",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        ToolInvokeResult result = new ToolInvokeResult();
        Stopwatch sw = Stopwatch.StartNew();

        using (Process process = Process.Start(psi))
        {
            string stdout = "";
            string stderr = "";
            Thread outThread = new Thread(() =>
            {
                try { stdout = process.StandardOutput.ReadToEnd(); }
                catch { }
            });
            Thread errThread = new Thread(() =>
            {
                try { stderr = process.StandardError.ReadToEnd(); }
                catch { }
            });
            outThread.IsBackground = true;
            errThread.IsBackground = true;
            outThread.Start();
            errThread.Start();

            process.StandardInput.Write(stdin);
            process.StandardInput.Close();

            bool exited;
            if (timeoutMs > 0)
                exited = process.WaitForExit(timeoutMs);
            else
            {
                process.WaitForExit();
                exited = true;
            }

            if (!exited)
            {
                result.TimedOut = true;
                try { process.Kill(); } catch { }
                try { process.WaitForExit(2000); } catch { }
            }

            if (!outThread.Join(PipeDrainTimeoutMs))
            {
                try { process.StandardOutput.Close(); } catch { }
            }
            if (!errThread.Join(PipeDrainTimeoutMs))
            {
                try { process.StandardError.Close(); } catch { }
            }
            outThread.Join(PipeCloseJoinTimeoutMs);
            errThread.Join(PipeCloseJoinTimeoutMs);
            sw.Stop();

            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.Stdout = stdout ?? "";
            result.Stderr = stderr ?? "";
            result.ExitCode = exited && !result.TimedOut ? process.ExitCode : -1;

            ParseStdoutJson(result);
        }

        TestRunner.AddProcessMs(result.ElapsedMs);
        TestLog.Detail("exit=" + result.ExitCode + " elapsed_ms=" + result.ElapsedMs +
            (result.TimedOut ? " TIMED_OUT" : ""));
        TestLog.Detail("stdout: " + TestLog.FormatStdoutForLog(result.Stdout));
        if (!string.IsNullOrEmpty(result.Stderr))
            TestLog.Detail("stderr: " + result.Stderr);

        return result;
    }

    public static ToolInvokeResult InvokeProduct(
        string exeFileName,
        string toolName,
        JObject arguments,
        JObject config = null,
        int timeoutMs = 0)
    {
        return Invoke(ProductExe(exeFileName), toolName, arguments, config, timeoutMs);
    }

    public static void AssertUnknownTool(string exeFileName)
    {
        ToolInvokeResult r = InvokeProduct(exeFileName, "not_a_real_tool", new JObject());
        TestAssert.Equal(1, r.ExitCode, "unknown tool exit code");
        TestAssert.ContainsIgnoreCase(r.Text ?? r.Stdout, "unknown tool", "unknown tool message");
    }

    private static void ParseStdoutJson(ToolInvokeResult result)
    {
        result.Text = result.Stdout ?? "";
        string trimmed = (result.Stdout ?? "").Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return;
        try
        {
            JObject obj = JObject.Parse(trimmed);
            JToken textToken = obj["text"];
            if (textToken != null && textToken.Type != JTokenType.Null)
                result.Text = textToken.Type == JTokenType.String
                    ? (textToken.Value<string>() ?? "")
                    : textToken.ToString();
            else
                result.Text = "";

            JObject image = obj["image"] as JObject;
            if (image != null)
            {
                result.ImageBase64 = (string)image["data"];
                result.ImageMime = (string)image["mime"];
            }
        }
        catch
        {
            // keep raw stdout as Text
        }
    }
}

public sealed class TempWorkspace : IDisposable
{
    public string Path { get; private set; }

    public TempWorkspace(string prefix)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            (prefix ?? "sllmc-test") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        TestLog.Detail("TempWorkspace: " + Path);
    }

    public string Combine(params string[] parts)
    {
        string p = Path;
        foreach (string part in parts)
            p = System.IO.Path.Combine(p, part);
        return p;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
        catch (Exception ex)
        {
            TestLog.Detail("TempWorkspace cleanup failed: " + ex.Message);
        }
    }
}

public sealed class ProcessResult
{
    public int ExitCode;
    public string Stdout;
    public string Stderr;
    public long ElapsedMs;
    public bool TimedOut;
}

public static class ProcessRunner
{
    public static ProcessResult Run(
        string fileName,
        string arguments,
        string workingDirectory,
        string stdin,
        int timeoutMs)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? "",
            WorkingDirectory = workingDirectory ?? ".",
            UseShellExecute = false,
            RedirectStandardInput = stdin != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        TestLog.Detail("Process: " + fileName + " " + (arguments ?? ""));
        TestLog.Detail("cwd: " + psi.WorkingDirectory);

        ProcessResult result = new ProcessResult();
        Stopwatch sw = Stopwatch.StartNew();
        using (Process process = Process.Start(psi))
        {
            string stdout = "";
            string stderr = "";
            Thread outThread = new Thread(() =>
            {
                try { stdout = process.StandardOutput.ReadToEnd(); }
                catch { }
            });
            Thread errThread = new Thread(() =>
            {
                try { stderr = process.StandardError.ReadToEnd(); }
                catch { }
            });
            outThread.IsBackground = true;
            errThread.IsBackground = true;
            outThread.Start();
            errThread.Start();

            if (stdin != null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }

            bool exited;
            if (timeoutMs > 0)
                exited = process.WaitForExit(timeoutMs);
            else
            {
                process.WaitForExit();
                exited = true;
            }

            if (!exited)
            {
                result.TimedOut = true;
                try { process.Kill(); } catch { }
                try { process.WaitForExit(2000); } catch { }
            }

            outThread.Join(2000);
            errThread.Join(2000);
            sw.Stop();

            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.Stdout = stdout ?? "";
            result.Stderr = stderr ?? "";
            result.ExitCode = exited && !result.TimedOut ? process.ExitCode : -1;
        }

        TestRunner.AddProcessMs(result.ElapsedMs);
        TestLog.Detail("exit=" + result.ExitCode + " elapsed_ms=" + result.ElapsedMs);
        TestLog.Detail("stdout: " + TestLog.FormatStdoutForLog(result.Stdout));
        if (!string.IsNullOrEmpty(result.Stderr))
            TestLog.Detail("stderr: " + result.Stderr);
        return result;
    }
}

public sealed class FakeOpenAiServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly Thread _thread;
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
        int port = FindFreePort();
        BaseUrl = "http://127.0.0.1:" + port;
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _running = true;
        _thread = new Thread(ListenLoop) { IsBackground = true };
        _thread.Start();
        TestLog.Detail("FakeOpenAiServer listening at " + BaseUrl);
    }

    private static int FindFreePort()
    {
        TcpListenerProbe probe = new TcpListenerProbe();
        return probe.Port;
    }

    private sealed class TcpListenerProbe
    {
        public int Port;
        public TcpListenerProbe()
        {
            System.Net.Sockets.TcpListener l =
                new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            Port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
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
        string content;
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
            content = sb.ToString();
        }
        else
        {
            content = FixedReply ?? "ok";
        }

        int delayMs = StreamDelayMs;
        int chunkChars = StreamChunkChars > 0 ? StreamChunkChars : 16;

        // Always stream + flush each SSE event so CLI and GUI exercise the same
        // token path (~LLM cadence when delayMs is 20–40).
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.SendChunked = true;

        using (Stream output = ctx.Response.OutputStream)
        {
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
        ctx.Response.Close();
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

public static class SuiteMain
{
    public static string ParseLogPath(string[] args)
    {
        if (args == null) return null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--log" && i + 1 < args.Length)
                return args[i + 1];
        }
        return null;
    }

    public static int Run(string suiteName, string[] args, Action runTests)
    {
        TestRunner.ResetCounts();
        string logPath = ParseLogPath(args);
        TestLog.Open(suiteName, logPath);
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            runTests();
        }
        catch (Exception ex)
        {
            TestLog.Info("SUITE EXCEPTION: " + ex);
            TestRunner.FailCount++;
        }
        sw.Stop();
        TestLog.Close(TestRunner.PassCount, TestRunner.FailCount, TestRunner.SkipCount, sw.ElapsedMilliseconds);
        Console.WriteLine(string.Format("[{0}] DONE  {1} pass, {2} fail, {3} skip  ({4} ms)",
            suiteName, TestRunner.PassCount, TestRunner.FailCount, TestRunner.SkipCount, sw.ElapsedMilliseconds));
        Console.WriteLine("Log: " + TestLog.LogPath);
        return TestRunner.ExitCode;
    }
}
