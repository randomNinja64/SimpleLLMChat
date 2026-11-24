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

            TestRunner.Run("ChatHistoryStore.read", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("gui-history"))
                {
                    TestAssert.Equal(0, ChatHistoryStore.Read(ws.Combine("missing")).Count, "no folder");

                    string titled = ws.Combine("titled.json");
                    File.WriteAllText(titled,
                        "{\"title\":\"Fix the build\",\"messages\":[{\"role\":\"user\",\"content\":\"x\"}]}");
                    string legacy = ws.Combine("legacy chat.json");
                    File.WriteAllText(legacy, "[{\"role\":\"user\",\"content\":\"old\"}]");
                    string broken = ws.Combine("broken.json");
                    File.WriteAllText(broken, "{\"title\":");
                    File.WriteAllText(ws.Combine("notes.txt"), "not a chat");

                    File.SetLastWriteTime(legacy, new DateTime(2026, 1, 1));
                    File.SetLastWriteTime(broken, new DateTime(2026, 2, 1));
                    File.SetLastWriteTime(titled, new DateTime(2026, 3, 1));

                    List<ChatHistoryEntry> entries = ChatHistoryStore.Read(ws.Path);
                    TestAssert.Equal(3, entries.Count, "json files only");
                    TestAssert.Equal("Fix the build", entries[0].Title, "newest, stored title");
                    TestAssert.Equal("broken", entries[1].Title, "unreadable falls back to file name");
                    TestAssert.Equal("legacy chat", entries[2].Title, "untitled falls back to file name");
                    TestAssert.Equal(titled, entries[0].Path, "path");
                }
            });

            TestRunner.Run("ToolArgumentsFormatter.fields_and_blocks", () =>
            {
                string text = ToolArgumentsFormatter.Format(
                    "{\"path\":\"C:\\\\temp\\\\new.cs\",\"content\":\"line one\\nline two\",\"overwrite\":true,\"count\":3}");
                TestAssert.Contains(text, "path: C:\\temp\\new.cs", "single-line field, backslashes kept");
                TestAssert.Contains(text, "content:\r\n    line one\r\n    line two", "multi-line field indented as a block");
                TestAssert.Contains(text, "overwrite: true", "boolean");
                TestAssert.Contains(text, "count: 3", "number");
            });

            TestRunner.Run("ToolArgumentsFormatter.nested_edits", () =>
            {
                string text = ToolArgumentsFormatter.Format(
                    "{\"path\":\"a.txt\",\"edits\":[{\"old_string\":\"x\",\"new_string\":\"y\\nz\"}]}");
                TestAssert.Contains(text, "edits:\r\n    [1]:\r\n        old_string: x", "array items numbered and indented");
                TestAssert.Contains(text, "        new_string:\r\n            y\r\n            z", "nested block");
            });

            TestRunner.Run("ToolArgumentsFormatter.fallbacks", () =>
            {
                TestAssert.Equal("(no arguments)", ToolArgumentsFormatter.Format(""), "empty");
                TestAssert.Equal("(no arguments)", ToolArgumentsFormatter.Format("{}"), "empty object");
                TestAssert.Equal("{\"command\": \"echo\n", ToolArgumentsFormatter.Format("{\"command\": \"echo\\n"),
                    "invalid JSON shown raw, unescaped as before");
            });

            TestRunner.Run("ModelsClient.list_models", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                {
                    IList<string> models = ModelsClient.ListModels(server.BaseUrl + "/v1", "test-key");
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

            TestRunner.Run("gui.model_you_line_kept", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageGuiWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueContent("dialogue:\n\nYou: from-model");
                    WriteGuiIni(ws.Path, server.BaseUrl + "/v1");

                    using (GuiDriver gui = new GuiDriver(ws.Path))
                    {
                        gui.WaitUntilReady(30000);
                        gui.SendTurn("hello", 60000);

                        TestAssert.True(gui.WaitForChatText("from-model", 10000), "reply shown");
                        string chat = gui.ReadChatText();
                        TestAssert.Contains(chat, "You: hello", "user turn");
                        TestAssert.Contains(chat, "You: from-model", "model's You: line kept");
                    }
                }
            });

            TestRunner.Run("gui.history_sidebar", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageGuiWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueContent("first-reply");
                    server.EnqueueContent("second-reply");
                    WriteGuiIni(ws.Path, server.BaseUrl + "/v1");

                    using (GuiDriver gui = new GuiDriver(ws.Path))
                    {
                        gui.WaitUntilReady(30000);
                        gui.ToggleHistory();
                        TestAssert.True(gui.WaitForHistory("", 10000), "empty before any chat");

                        // Saved after the turn; the open chat is listed and marked.
                        gui.SendTurn("first chat", 60000);
                        TestAssert.True(gui.WaitForHistory("first chat*", 10000),
                            "first chat listed as current: " + gui.ReadHistory());

                        // New Chat: nothing is current until the new chat saves.
                        gui.NewChat();
                        TestAssert.True(gui.WaitForHistory("first chat", 10000),
                            "no current chat after New Chat: " + gui.ReadHistory());
                        gui.WaitUntilReady(30000);
                        gui.SendTurn("second chat", 60000);
                        TestAssert.True(gui.WaitForHistory("second chat*|first chat", 10000),
                            "newest first, new chat current: " + gui.ReadHistory());
                    }
                }
            });

            TestRunner.Run("gui.model_tag_text_kept", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageGuiWorkspace())
                {
                    server.StreamDelayMs = 0;
                    // Tag-like text from the model is content, not block structure.
                    server.EnqueueContent("before [tool output] middle [/tool output] [thinking] after");
                    WriteGuiIni(ws.Path, server.BaseUrl + "/v1");

                    using (GuiDriver gui = new GuiDriver(ws.Path))
                    {
                        gui.WaitUntilReady(30000);
                        gui.SendTurn("show [/thinking] tags", 60000);

                        TestAssert.True(gui.WaitForChatText("after", 10000), "reply shown");
                        string chat = gui.ReadChatText();
                        TestAssert.Contains(chat, "You: show [/thinking] tags", "user tags kept");
                        TestAssert.Contains(
                            chat, "before [tool output] middle [/tool output] [thinking] after", "model tags kept");
                    }
                }
            });

            TestRunner.Run("gui.reasoning_field_thinking_block", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageGuiWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueReasoningThenContent("mulling it over", "reasoned-answer");
                    WriteGuiIni(ws.Path, server.BaseUrl + "/v1");

                    using (GuiDriver gui = new GuiDriver(ws.Path))
                    {
                        gui.WaitUntilReady(30000);
                        gui.SendTurn("hello", 60000);

                        TestAssert.True(gui.WaitForChatText("reasoned-answer", 10000), "reply shown");
                        string chat = gui.ReadChatText();
                        TestLog.Detail("chat text: " + chat);
                        // Collapsed by default: the finished block's header reads "thought for N seconds".
                        TestAssert.Contains(chat, "thought for ", "thinking block header");
                        TestAssert.True(chat.IndexOf("thinking]", StringComparison.Ordinal) < 0, "no tags shown");
                    }
                }
            });

            TestRunner.Run("gui.approval_dialog_runs_tool", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageGuiWorkspace())
                {
                    InstallShellToolsRequiringApproval(ws);

                    server.StreamDelayMs = 0;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_gui_approve",
                        "{\"command\":\"echo GUI_APPROVE_OK\"}");
                    server.EnqueueContent("gui-approve-final");
                    WriteGuiIni(ws.Path, server.BaseUrl + "/v1");
                    RequireApprovalForShell(ws);

                    using (GuiDriver gui = new GuiDriver(ws.Path))
                    {
                        gui.WaitUntilReady(30000);
                        gui.Submit("please run the command");

                        GuiDriver.ApprovalPrompt dialog = gui.AnswerApproval(true, 30000);
                        TestAssert.Contains(dialog.Header, "run_shell_command", "dialog names tool");
                        TestAssert.Contains(dialog.Arguments, "command: echo GUI_APPROVE_OK", "dialog shows arguments");

                        gui.WaitForTurnComplete(60000);
                        TestAssert.True(gui.WaitForChatText("gui-approve-final", 10000), "final reply shown");
                        TestAssert.ContainsIgnoreCase(server.LastBody, "GUI_APPROVE_OK", "approved tool ran");
                        TestAssert.True(
                            server.LastBody.IndexOf("cancelled by the user", StringComparison.Ordinal) < 0,
                            "not cancelled");
                        TestAssert.True(
                            gui.ReadChatText().IndexOf("Approve?", StringComparison.Ordinal) < 0,
                            "no approval prompt in transcript");
                    }
                }
            });

            TestRunner.Run("gui.approval_dialog_large_call_fits_screen", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageGuiWorkspace())
                {
                    InstallShellToolsRequiringApproval(ws);

                    // Far taller and wider than any screen: 400 lines, one of them 3,000 characters.
                    StringBuilder command = new StringBuilder();
                    for (int i = 1; i <= 400; i++)
                        command.Append("echo LINE_").Append(i).Append('\n');
                    command.Append("echo ").Append(new string('W', 3000)).Append(" WIDE_END");

                    server.StreamDelayMs = 0;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_gui_large",
                        new Newtonsoft.Json.Linq.JObject { ["command"] = command.ToString() }
                            .ToString(Newtonsoft.Json.Formatting.None));
                    server.EnqueueContent("gui-large-final");
                    WriteGuiIni(ws.Path, server.BaseUrl + "/v1");
                    RequireApprovalForShell(ws);

                    using (GuiDriver gui = new GuiDriver(ws.Path))
                    {
                        gui.WaitUntilReady(30000);
                        gui.Submit("please run the long command");

                        GuiDriver.ApprovalPrompt dialog = gui.AnswerApproval(false, 30000);
                        System.Windows.Rect screen = GuiDriver.PrimaryWorkArea();
                        TestLog.Detail("work area " + screen + ", dialog " + dialog.Dialog
                            + ", yes " + dialog.ApproveButton + ", no " + dialog.DenyButton);

                        TestAssert.Contains(dialog.Arguments, "    echo LINE_1\r\n", "first line, as a block");
                        TestAssert.Contains(dialog.Arguments, "echo LINE_400", "last line: nothing cut");
                        TestAssert.Contains(dialog.Arguments, "WIDE_END", "long line not cut");
                        TestAssert.True(!dialog.ButtonsOffscreen, "buttons reported on screen");
                        TestAssert.True(screen.Contains(dialog.ApproveButton), "Yes inside the work area");
                        TestAssert.True(screen.Contains(dialog.DenyButton), "No inside the work area");
                        TestAssert.True(dialog.Dialog.Height <= screen.Height, "dialog no taller than the screen");
                        TestAssert.True(dialog.Dialog.Width <= screen.Width, "dialog no wider than the screen");

                        gui.WaitForTurnComplete(60000);
                        TestAssert.True(gui.WaitForChatText("gui-large-final", 10000), "final reply shown");
                        TestAssert.Contains(server.LastBody, "cancelled by the user", "No denied the call");
                    }
                }
            });

            TestRunner.Run("gui.window_and_long_chat", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageGuiWorkspace())
                {
                    server.LongMarkdownReplies = true;
                    WriteGuiIni(ws.Path, server.BaseUrl + "/v1");

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

        /// <summary>
        /// The GUI test host has no tools of its own; borrow the CLI suite's ShellTools.
        /// Skips the test when that copy is missing.
        /// </summary>
        static void InstallShellToolsRequiringApproval(TempWorkspace ws)
        {
            string shellTools = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "SimpleLLMChatCLI.Tests", "tools", "ShellTools");
            if (!File.Exists(Path.Combine(shellTools, "ShellTools.exe")))
                TestRunner.Skip("tools/ShellTools not packaged beside CLI test host");
            string toolsDst = Path.Combine(ws.Path, "tools", "ShellTools");
            Directory.CreateDirectory(toolsDst);
            foreach (string file in Directory.GetFiles(shellTools))
                File.Copy(file, Path.Combine(toolsDst, Path.GetFileName(file)), true);
        }

        /// <summary>Call after <see cref="WriteGuiIni"/>: later keys win over its empty defaults.</summary>
        static void RequireApprovalForShell(TempWorkspace ws)
        {
            File.AppendAllText(
                Path.Combine(ws.Path, "LLMSettings.ini"),
                "\r\ntools=run_shell_command\r\ntoolsrequiringapproval=run_shell_command\r\n");
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
