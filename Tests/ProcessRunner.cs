using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

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
        if (string.IsNullOrEmpty(Path) || !Directory.Exists(Path))
            return;

        Exception last = null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Directory.Delete(Path, true);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                // XP keeps an exclusive mapping on an EXE for a short time after
                // Process.WaitForExit/Dispose. Retry instead of leaking the folder.
                Thread.Sleep(50 * (attempt + 1));
            }
        }

        TestLog.Detail("TempWorkspace cleanup failed: " +
            (last != null ? last.Message : "unknown"));
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
            else
            {
                // Timed WaitForExit can return before redirected stdio is fully
                // drained / the image mapping is released (especially on XP).
                process.WaitForExit();
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
