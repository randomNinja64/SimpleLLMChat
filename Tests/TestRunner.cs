using System;
using System.Diagnostics;
using System.IO;

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
            TestLog.Result("(suite)", "FAIL", sw.ElapsedMilliseconds, 0, "  " + ex);
        }
        sw.Stop();
        TestLog.Close(TestRunner.PassCount, TestRunner.FailCount, TestRunner.SkipCount, sw.ElapsedMilliseconds);
        Console.WriteLine(string.Format("[{0}] DONE  {1} pass, {2} fail, {3} skip  ({4} ms)",
            suiteName, TestRunner.PassCount, TestRunner.FailCount, TestRunner.SkipCount, sw.ElapsedMilliseconds));
        Console.WriteLine("Log: " + TestLog.LogPath);
        if (!string.IsNullOrEmpty(TestLog.IssuesPath) && File.Exists(TestLog.IssuesPath))
            Console.WriteLine("Issues: " + TestLog.IssuesPath);
        return TestRunner.ExitCode;
    }
}
