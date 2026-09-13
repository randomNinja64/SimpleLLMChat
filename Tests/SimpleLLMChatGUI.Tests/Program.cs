using SimpleLLMChatGUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SimpleLLMChatGUI.Tests
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            return SuiteMain.Run("SimpleLLMChatGUI", args, Run);
        }

        static void Run()
        {
            TestRunner.Run("SettingsIniWriter.escape", () =>
            {
                string escaped = SettingsIniWriter.EscapePromptForStorage("a\\b\nc\td");
                TestAssert.Contains(escaped, "\\\\", "backslash");
                TestAssert.Contains(escaped, "\\n", "newline");
                TestAssert.Contains(escaped, "\\t", "tab");
            });

            TestRunner.Run("SettingsIniWriter.write_roundtrip", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("gui-ini"))
                {
                    string path = ws.Combine("LLMSettings.ini");
                    SettingsIniWriter.WriteInitialConfig(
                        path, "key", "http://127.0.0.1:9", "test-model", "Hello", 4096);
                    TestAssert.True(File.Exists(path), "exists");
                    string text = File.ReadAllText(path);
                    TestAssert.Contains(text, "llmserver=http://127.0.0.1:9", "server");
                    TestAssert.Contains(text, "model=test-model", "model");
                }
            });

            TestRunner.Run("ToolApproval.parse", () =>
            {
                string msg = ToolApproval.FormatApprovalMessage("shell", "echo hi");
                string tool, arguments;
                TestAssert.True(ToolApproval.TryParseApprovalPrompt(
                    msg + "\n" + ToolApproval.ApprovalPrompt, out tool, out arguments), "parse");
                TestAssert.Equal("shell", tool, "tool");
                TestAssert.Contains(arguments, "echo hi", "args");
            });

            TestRunner.Run("ModelsClient.list_models", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                {
                    IList<string> models = ModelsClient.ListModels(server.BaseUrl, "test-key");
                    TestAssert.True(models.Count >= 1, "count");
                    TestAssert.Equal("test-model", models[0], "id");
                }
            });

            TestRunner.Run("ModelsClient.empty_url", () =>
            {
                bool threw = false;
                try { ModelsClient.ListModels("", ""); }
                catch (Exception) { threw = true; }
                TestAssert.True(threw, "should throw");
            });

            TestRunner.Run("gui.window_and_long_chat", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageGuiWorkspace())
                {
                    server.LongMarkdownReplies = true;
                    WriteGuiIni(ws.Path, server.BaseUrl);

                    string sessionPerf = Path.Combine(
                        Path.GetDirectoryName(TestLog.LogPath) ?? ".",
                        "gui-session.perf.tsv");
                    using (StreamWriter perf = new StreamWriter(sessionPerf, false, Encoding.UTF8))
                    {
                        perf.WriteLine("turn\twait_ms\tgui_workingset_mb\tcli_workingset_mb");

                        using (GuiDriver gui = new GuiDriver(ws.Path))
                        {
                            gui.WaitUntilReady(30000);

                            Process cli = null;
                            Stopwatch ready = Stopwatch.StartNew();
                            while (ready.ElapsedMilliseconds < 15000 && cli == null)
                            {
                                cli = gui.FindCliProcess();
                                if (cli == null)
                                    System.Threading.Thread.Sleep(100);
                            }
                            TestAssert.True(cli != null, "CLI process started under work dir");

                            const int turns = 50;
                            for (int i = 1; i <= turns; i++)
                            {
                                Stopwatch turnSw = Stopwatch.StartNew();
                                gui.SendTurn("turn " + i, 60000);
                                turnSw.Stop();

                                long guiMb = WorkingSetMb(gui.GuiProcess);
                                Process cliProc = gui.FindCliProcess() ?? cli;
                                long cliMb = WorkingSetMb(cliProc);
                                perf.WriteLine(string.Format("{0}\t{1}\t{2}\t{3}",
                                    i, turnSw.ElapsedMilliseconds, guiMb, cliMb));
                                perf.Flush();
                                TestLog.Detail(string.Format(
                                    "turn {0} wait_ms={1} gui_mb={2} cli_mb={3}",
                                    i, turnSw.ElapsedMilliseconds, guiMb, cliMb));
                            }
                        }
                    }
                    TestLog.Info("gui-session.perf.tsv=" + sessionPerf);
                }
            });
        }

        static long WorkingSetMb(Process p)
        {
            try
            {
                if (p == null || p.HasExited) return 0;
                p.Refresh();
                return p.WorkingSet64 / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }

        static TempWorkspace PackageGuiWorkspace()
        {
            TempWorkspace ws = new TempWorkspace("gui-run");
            string[] files = { "SimpleLLMChatGUI.exe", "SimpleLLMChatCLI.exe", "Newtonsoft.Json.dll" };
            foreach (string f in files)
            {
                string src = ToolClient.ProductExe(f);
                if (!File.Exists(src) && f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    throw new TestFailureException("Missing packaged file: " + src);
                if (File.Exists(src))
                    File.Copy(src, Path.Combine(ws.Path, f), true);
            }
            return ws;
        }

        static void WriteGuiIni(string dir, string llmServer)
        {
            string path = Path.Combine(dir, "LLMSettings.ini");
            SettingsIniWriter.WriteInitialConfig(
                path,
                "test-key",
                llmServer,
                "test-model",
                "You are a test assistant.",
                8192);
        }
    }
}
