using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace ShellTools.Tests
{
    internal static class Program
    {
        private const string Exe = "ShellTools.exe";

        static int Main(string[] args)
        {
            return SuiteMain.Run("ShellTools", args, Run);
        }

        static void Run()
        {
            TestRunner.Run("unknown_tool", () => ToolClient.AssertUnknownTool(Exe));

            using (TempWorkspace ws = new TempWorkspace("shelltools"))
            {
                TestRunner.Run("echo.hello", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "run_shell_command",
                        new JObject { ["command"] = "echo hello" });
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Contains(r.Text, "hello", "stdout");
                });

                TestRunner.Run("failing.exit_code", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "run_shell_command",
                        new JObject { ["command"] = "exit /b 7" });
                    TestAssert.Equal(7, r.ExitCode, "exit code 7");
                    TestAssert.True(r.Stdout.Trim().StartsWith("{"), "json envelope");
                });

                TestRunner.Run("paths.with_spaces", () =>
                {
                    string folder = ws.Combine("my folder");
                    Directory.CreateDirectory(folder);
                    string file = Path.Combine(folder, "hello world.txt");
                    File.WriteAllText(file, "spaced-content", Encoding.UTF8);

                    ToolInvokeResult type = ToolClient.InvokeProduct(Exe, "run_shell_command",
                        new JObject { ["command"] = "type \"" + file + "\"" });
                    TestAssert.Equal(0, type.ExitCode, "type exit");
                    TestAssert.Contains(type.Text, "spaced-content", "type content");

                    ToolInvokeResult dir = ToolClient.InvokeProduct(Exe, "run_shell_command",
                        new JObject { ["command"] = "dir /b \"" + folder + "\"" });
                    TestAssert.Equal(0, dir.ExitCode, "dir exit");
                    TestAssert.Contains(dir.Text, "hello world.txt", "dir name");
                });

                TestRunner.Run("cd.spaced_directory", () =>
                {
                    string folder = ws.Combine("path with spaces");
                    Directory.CreateDirectory(folder);
                    // Use `cd` (prints cwd) — `%CD%` is expanded at parse time before `cd /d` runs.
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "run_shell_command",
                        new JObject
                        {
                            ["command"] = "cd /d \"" + folder + "\" && cd"
                        });
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.ContainsIgnoreCase(r.Text.Replace('/', '\\'),
                        folder.Replace('/', '\\'), "cwd");
                });

                TestRunner.Run("stderr.captured", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "run_shell_command",
                        new JObject { ["command"] = "echo oops 1>&2" });
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Contains(r.Text, "oops", "stderr combined into text");
                });

                TestRunner.Run("missing.command", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "run_shell_command",
                        new JObject());
                    TestAssert.True(r.ExitCode != 0, "non-zero");
                    TestAssert.ContainsIgnoreCase(r.Text ?? r.Stdout, "missing", "error");
                });

                TestRunner.Run("gui.launch_notepad", () =>
                {
                    // Snapshot existing notepad PIDs so we only kill ones we start.
                    System.Collections.Generic.HashSet<int> before = SnapshotPids("notepad");

                    // `start` under redirected stdout inherits pipe handles and cmd waits for
                    // notepad (ShellTools never exits). Start-Process detaches correctly.
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "run_shell_command",
                        new JObject
                        {
                            ["command"] = "powershell -NoProfile -Command Start-Process notepad"
                        },
                        null,
                        15000);

                    try
                    {
                        if (r.TimedOut)
                            throw new TestFailureException(
                                "ShellTools.exe still running after launching notepad; kill issued.");

                        TestAssert.Equal(0, r.ExitCode, "start exit");

                        int newPid = WaitForNewProcess("notepad", before, 10000);
                        TestAssert.True(newPid > 0, "notepad appeared");
                    }
                    finally
                    {
                        KillNewProcesses("notepad", before);
                    }
                });
            }
        }

        static System.Collections.Generic.HashSet<int> SnapshotPids(string name)
        {
            var set = new System.Collections.Generic.HashSet<int>();
            foreach (Process p in Process.GetProcessesByName(name))
            {
                try { set.Add(p.Id); } catch { }
                try { p.Dispose(); } catch { }
            }
            return set;
        }

        static int WaitForNewProcess(string name, System.Collections.Generic.HashSet<int> before, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (!before.Contains(p.Id))
                            return p.Id;
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
                Thread.Sleep(100);
            }
            return -1;
        }

        static void KillNewProcesses(string name, System.Collections.Generic.HashSet<int> before)
        {
            foreach (Process p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (!before.Contains(p.Id))
                        p.Kill();
                }
                catch { }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }
    }
}
