using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

/// <summary>
/// Interactive CLI process with redirected stdin/stdout for StatusPipe tests.
/// </summary>
public sealed class CliProcess : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _stdout = new StringBuilder();
    private readonly StringBuilder _stderr = new StringBuilder();
    private readonly object _stdoutGate = new object();
    private readonly Thread _outThread;
    private readonly Thread _errThread;
    private bool _disposed;

    public int Id { get { return _process.Id; } }

    public CliProcess(string exePath, string arguments, string workingDirectory)
    {
        if (!File.Exists(exePath))
            throw new TestFailureException("CLI EXE not found: " + exePath);

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments ?? "",
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(exePath) ?? ".",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        TestLog.Detail("CliProcess: " + exePath + " " + (arguments ?? ""));
        _process = Process.Start(psi);
        if (_process == null)
            throw new TestFailureException("Failed to start CLI process.");

        _outThread = new Thread(() => Drain(_process.StandardOutput, _stdout, _stdoutGate))
        {
            IsBackground = true
        };
        _errThread = new Thread(() => Drain(_process.StandardError, _stderr, null))
        {
            IsBackground = true
        };
        _outThread.Start();
        _errThread.Start();
    }

    public void WriteLine(string line)
    {
        _process.StandardInput.WriteLine(line ?? "");
        _process.StandardInput.Flush();
    }

    public string Stdout
    {
        get { lock (_stdoutGate) return _stdout.ToString(); }
    }

    public bool WaitForExit(int timeoutMs)
    {
        bool exited = _process.WaitForExit(timeoutMs);
        if (exited)
            _process.WaitForExit();
        _outThread.Join(2000);
        _errThread.Join(2000);
        return exited;
    }

    private static void Drain(StreamReader reader, StringBuilder target, object gate)
    {
        try
        {
            string chunk;
            char[] buffer = new char[256];
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                chunk = new string(buffer, 0, read);
                if (gate != null)
                {
                    lock (gate)
                        target.Append(chunk);
                }
                else
                    target.Append(chunk);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                try { _process.StandardInput.WriteLine("/exit"); } catch { }
                if (!_process.WaitForExit(2000))
                {
                    try { _process.Kill(); } catch { }
                    try { _process.WaitForExit(2000); } catch { }
                }
            }
        }
        catch { }

        try { _process.Dispose(); } catch { }
    }
}
