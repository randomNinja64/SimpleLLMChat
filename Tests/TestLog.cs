using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Shared test harness logging for SimpleLLMChat suite EXEs (net40).
/// </summary>
public static class TestLog
{
    public const long SlowThresholdMs = 5000;

    private static StreamWriter _writer;
    private static StreamWriter _perfWriter;
    private static StreamWriter _issuesWriter;
    private static string _logPath;
    private static string _perfPath;
    private static string _issuesPath;
    private static string _suiteName = "Suite";
    private static readonly object _gate = new object();
    private static readonly List<PerfRow> _rows = new List<PerfRow>();

    private sealed class PerfRow
    {
        public string Case;
        public string Status;
        public long CaseMs;
        public long ProcessMs;
        public bool Slow;
    }

    public static string LogPath { get { return _logPath; } }
    public static string PerfPath { get { return _perfPath; } }
    public static string IssuesPath { get { return _issuesPath; } }

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
        // Prefer Suite.perf.tsv / Suite.issues.log beside Suite.log
        string baseName = Path.GetFileNameWithoutExtension(_logPath);
        string logDir = Path.GetDirectoryName(_logPath) ?? ".";
        _perfPath = Path.Combine(logDir, baseName + ".perf.tsv");
        _issuesPath = Path.Combine(logDir, baseName + ".issues.log");

        if (_issuesWriter != null)
        {
            _issuesWriter.Dispose();
            _issuesWriter = null;
        }
        if (File.Exists(_issuesPath))
        {
            try { File.Delete(_issuesPath); }
            catch { }
        }

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
        Info("Issues=" + _issuesPath + " (written if any FAIL or case_ms>=" + SlowThresholdMs + ")");
    }

    public static void Close(int pass, int fail, int skip, long suiteMs)
    {
        WriteHotSpots(10);
        WriteFailedOrSlow();
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
        if (_issuesWriter != null)
        {
            _issuesWriter.Dispose();
            _issuesWriter = null;
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
        if (slow)
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
                ProcessMs = processMs,
                Slow = slow
            });

            if (status == "FAIL" || slow)
                WriteIssue(tag, caseName, caseMs, processMs, detail);
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

    private static void WriteFailedOrSlow()
    {
        List<PerfRow> notable = new List<PerfRow>();
        for (int i = 0; i < _rows.Count; i++)
        {
            PerfRow r = _rows[i];
            if (r.Status == "FAIL" || r.Slow)
                notable.Add(r);
        }

        if (notable.Count == 0)
        {
            Info("Failed or slow: none");
            return;
        }

        notable.Sort((a, b) =>
        {
            int af = a.Status == "FAIL" ? 0 : 1;
            int bf = b.Status == "FAIL" ? 0 : 1;
            int cmp = af.CompareTo(bf);
            if (cmp != 0) return cmp;
            return b.CaseMs.CompareTo(a.CaseMs);
        });

        Info("Failed or slow (" + notable.Count + ", threshold_ms=" + SlowThresholdMs + "):");
        for (int i = 0; i < notable.Count; i++)
        {
            PerfRow r = notable[i];
            string tag = r.Slow ? r.Status + "+SLOW" : r.Status;
            Info(string.Format("  {0}  {1}  case_ms={2} process_ms={3}",
                tag, r.Case, r.CaseMs, r.ProcessMs));
        }

        lock (_gate)
        {
            if (_issuesWriter != null)
                _issuesWriter.WriteLine("# count=" + notable.Count);
        }
    }

    private static void WriteIssue(string tag, string caseName, long caseMs, long processMs, string detail)
    {
        EnsureIssuesWriter();
        _issuesWriter.WriteLine(string.Format("{0}  {1}  case_ms={2} process_ms={3}",
            tag, caseName, caseMs, processMs));
        if (!string.IsNullOrEmpty(detail))
            _issuesWriter.WriteLine(detail);
    }

    private static void EnsureIssuesWriter()
    {
        if (_issuesWriter != null)
            return;

        _issuesWriter = new StreamWriter(_issuesPath, false, Encoding.UTF8) { AutoFlush = true };
        _issuesWriter.WriteLine("Failed or slow tests (threshold_ms=" + SlowThresholdMs + ")");
        _issuesWriter.WriteLine("Suite=" + _suiteName);
        _issuesWriter.WriteLine();
    }
}
