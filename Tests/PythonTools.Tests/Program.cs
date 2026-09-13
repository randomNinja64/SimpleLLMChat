using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;

namespace PythonTools.Tests
{
    internal static class Program
    {
        private const string Exe = "PythonTools.exe";

        static int Main(string[] args)
        {
            return SuiteMain.Run("PythonTools", args, Run);
        }

        static void Run()
        {
            TestRunner.Run("unknown_tool", () => ToolClient.AssertUnknownTool(Exe));

            TestRunner.Run("run_python_script.add", () =>
            {
                if (!PythonOnPath())
                    TestRunner.Skip("python not on PATH");

                ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "run_python_script",
                    new JObject { ["script_content"] = "print(1+1)" });
                TestAssert.Equal(0, r.ExitCode, "exit");
                TestAssert.Contains(r.Text, "2", "output");
            });
        }

        static bool PythonOnPath()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(5000);
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
