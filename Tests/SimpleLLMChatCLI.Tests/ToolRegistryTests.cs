using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SimpleLLMChatCLI.Tests
{
    /// <summary>
    /// ToolRegistry and Cancel, driven in-process against a batch-file tool package.
    /// </summary>
    internal static class ToolRegistryTests
    {
        private static bool _listening;

        public static void Run()
        {
            RegistryTests();
            CancelTests();
        }

        /// <summary>
        /// A tool package whose executable is a batch file: no build step, and the
        /// process the registry starts (cmd.exe) is the one that does the work.
        /// </summary>
        private static string WriteFixturePackage(TempWorkspace ws)
        {
            string toolsDir = ws.Combine("tools");
            string dir = Path.Combine(toolsDir, "Fixture");
            Directory.CreateDirectory(dir);

            File.WriteAllText(Path.Combine(dir, "fixture.cmd"), string.Join("\r\n", new[]
            {
                "@echo off",
                "if \"%~1\"==\"fix_echo\" goto do_echo",
                "if \"%~1\"==\"fix_fail\" goto do_fail",
                "if \"%~1\"==\"fix_stdin\" goto do_stdin",
                "if \"%~1\"==\"fix_hang\" goto do_spin",
                "if \"%~1\"==\"fix_spin\" goto do_spin",
                "if \"%~1\"==\"inject\" goto do_inject",
                "exit 9",
                ":do_echo",
                "echo {\"text\":\"echo-ok\"}",
                "exit 0",
                ":do_fail",
                ">&2 echo boom",
                "exit 3",
                ":do_stdin",
                "echo STDIN:",
                "sort",
                "exit 0",
                ":do_inject",
                "echo {\"text\":\"  injected-context  \"}",
                "exit 0",
                ":do_spin",
                "goto do_spin",
                ""
            }), Encoding.ASCII);

            JArray tools = new JArray();
            tools.Add(new JObject
            {
                ["name"] = "fix_echo",
                ["description"] = "Echo {config.maxlines} lines, {config.maxlines*2} max, {config.missing}",
                ["parameters"] = new JArray(
                    new JObject
                    {
                        ["name"] = "text",
                        ["type"] = "string",
                        ["description"] = "Up to {config.maxlines}",
                        ["required"] = true
                    },
                    new JObject
                    {
                        ["name"] = "tags",
                        ["type"] = "array",
                        ["description"] = "Labels",
                        ["items"] = new JObject { ["type"] = "string" }
                    },
                    new JObject { ["name"] = "untyped" })
            });
            foreach (string name in new[] { "fix_fail", "fix_stdin", "fix_hang", "fix_spin" })
                tools.Add(new JObject { ["name"] = name, ["description"] = name });
            tools.Add(new JObject { ["description"] = "no name, ignored" });

            JObject manifest = new JObject
            {
                ["executable"] = "fixture.cmd",
                ["context_injector"] = "inject",
                ["options"] = new JArray(
                    new JObject { ["name"] = "fixopt", ["default"] = "dflt" },
                    new JObject { ["name"] = "fixopt2", ["default"] = "dflt2" }),
                ["tools"] = tools
            };
            File.WriteAllText(Path.Combine(dir, "Fixture.json"), manifest.ToString(), Encoding.UTF8);

            return toolsDir;
        }

        private static ToolRegistry LoadFixture(TempWorkspace ws)
        {
            string toolsDir = WriteFixturePackage(ws);
            string ini = ws.Combine("LLMSettings.ini");
            File.WriteAllText(ini,
                "[Tools]\r\nmaxlines=50\r\nfixopt2=fromini\r\ntooltimeout.fix_hang=1\r\n",
                Encoding.UTF8);
            ToolRegistry registry = new ToolRegistry(new ConfigHandler(ini));
            registry.LoadToolsFromDirectory(toolsDir);
            return registry;
        }

        private static void RegistryTests()
        {
            TestRunner.Run("registry.tools_array_schema", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("cli-registry"))
                {
                    ToolRegistry registry = LoadFixture(ws);

                    JArray array = registry.BuildToolsArray(new List<string> { "FIX_FAIL", "not_loaded", "fix_echo" });
                    TestAssert.Equal(2, array.Count, "unknown names skipped");
                    TestAssert.Equal("fix_fail", (string)array[0]["function"]["name"], "order follows the enabled list");

                    JObject tool = (JObject)array[1];
                    TestAssert.Equal("function", (string)tool["type"], "type");
                    JObject function = (JObject)tool["function"];
                    TestAssert.Equal("fix_echo", (string)function["name"], "name");
                    TestAssert.Equal(
                        "Echo 50 lines, 100 max, {config.missing}",
                        (string)function["description"], "placeholders");

                    JObject parameters = (JObject)function["parameters"];
                    TestAssert.Equal("object", (string)parameters["type"], "parameters type");
                    TestAssert.Equal("[\"text\"]", parameters["required"].ToString(Newtonsoft.Json.Formatting.None), "required");
                    JObject properties = (JObject)parameters["properties"];
                    TestAssert.Equal("string", (string)properties["text"]["type"], "text type");
                    TestAssert.Equal("Up to 50", (string)properties["text"]["description"], "parameter placeholder");
                    TestAssert.Equal("array", (string)properties["tags"]["type"], "tags type");
                    TestAssert.Equal("string", (string)properties["tags"]["items"]["type"], "items kept");
                    TestAssert.True(properties["text"]["items"] == null, "no items on a scalar");
                    TestAssert.Equal("string", (string)properties["untyped"]["type"], "type defaults to string");

                    TestAssert.Equal("fix_fail", registry.GetToolDescription("fix_fail"), "description lookup");
                    TestAssert.Equal("", registry.GetToolDescription("not_loaded"), "unknown description");

                    registry.Clear();
                    TestAssert.Equal(0, registry.BuildToolsArray(new List<string> { "fix_echo" }).Count, "cleared");
                }
            });

            TestRunner.Run("registry.bad_manifest_is_skipped", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("cli-registry"))
                {
                    string toolsDir = WriteFixturePackage(ws);
                    Directory.CreateDirectory(Path.Combine(toolsDir, "Broken"));
                    File.WriteAllText(Path.Combine(toolsDir, "Broken", "Broken.json"), "{ this is not json");
                    Directory.CreateDirectory(Path.Combine(toolsDir, "NoTools"));
                    File.WriteAllText(Path.Combine(toolsDir, "NoTools", "NoTools.json"), "{\"executable\":\"x.exe\"}");

                    ToolRegistry registry = new ToolRegistry(new ConfigHandler(ws.Combine("missing.ini")));
                    TextWriter originalError = Console.Error;
                    StringWriter error = new StringWriter();
                    Console.SetError(error);
                    try
                    {
                        registry.LoadToolsFromDirectory(toolsDir);
                        registry.LoadToolsFromDirectory(ws.Combine("no-such-dir"));
                    }
                    finally
                    {
                        Console.SetError(originalError);
                    }

                    TestAssert.Contains(error.ToString(), "Skipping tool manifest", "reported");
                    TestAssert.Contains(error.ToString(), "Broken.json", "names the file");
                    TestAssert.Equal(1, registry.BuildToolsArray(new List<string> { "fix_echo" }).Count, "good package still loads");
                }
            });

            TestRunner.Run("registry.execute_success", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("cli-registry"))
                {
                    ToolRegistry registry = LoadFixture(ws);
                    string content;
                    int exitCode;
                    registry.ExecuteToolCall("fix_echo", "{\"text\":\"hi\"}", out content, out exitCode);
                    TestAssert.Equal(0, exitCode, "exit");
                    TestAssert.Equal("Command: fix_echo\nExit Code: 0\nOutput:\necho-ok", content, "formatted result");
                }
            });

            TestRunner.Run("registry.execute_failures", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("cli-registry"))
                {
                    ToolRegistry registry = LoadFixture(ws);
                    string content;
                    int exitCode;

                    registry.ExecuteToolCall("fix_fail", "{}", out content, out exitCode);
                    TestAssert.Equal(3, exitCode, "tool exit code");
                    TestAssert.Contains(content, "Exit Code: 3\nOutput:\nboom", "stderr becomes the text");

                    registry.ExecuteToolCall("not_loaded", "{}", out content, out exitCode);
                    TestAssert.Equal(1, exitCode, "unknown tool exit code");
                    TestAssert.Contains(content, "error: unknown tool 'not_loaded'", "unknown tool");

                    // Arguments the model cut off mid-JSON never reach the tool.
                    registry.ExecuteToolCall("fix_echo", "{\"text\":", out content, out exitCode);
                    TestAssert.Equal(-1, exitCode, "bad arguments exit code");
                    TestAssert.Contains(content, "Output:\nerror: ", "bad arguments reported");
                    TestAssert.True(content.IndexOf("echo-ok", StringComparison.Ordinal) < 0, "tool did not run");
                }
            });

            TestRunner.Run("registry.stdin_payload", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("cli-registry"))
                {
                    ToolRegistry registry = LoadFixture(ws);
                    string content;
                    int exitCode;

                    registry.ExecuteToolCall("fix_stdin", "{\"text\":\"hi\",\"n\":2}", out content, out exitCode);
                    TestAssert.Equal(0, exitCode, "exit");
                    TestAssert.Contains(content, "STDIN:", "fixture ran");
                    JObject payload = JObject.Parse(content.Substring(content.IndexOf('{')));
                    TestAssert.Equal("hi", (string)payload["arguments"]["text"], "argument");
                    TestAssert.Equal(2, (int)payload["arguments"]["n"], "typed argument");
                    TestAssert.Equal("dflt", (string)payload["config"]["fixopt"], "manifest default");
                    TestAssert.Equal("fromini", (string)payload["config"]["fixopt2"], "setting beats the default");
                    TestAssert.Equal("50", (string)payload["config"]["maxlines"], "setting forwarded");

                    // No arguments from the model is an empty object, not a failure.
                    registry.ExecuteToolCall("fix_stdin", "", out content, out exitCode);
                    TestAssert.Equal(0, exitCode, "empty arguments exit");
                    payload = JObject.Parse(content.Substring(content.IndexOf('{')));
                    TestAssert.Equal("{}", payload["arguments"].ToString(Newtonsoft.Json.Formatting.None), "empty arguments");
                }
            });

            TestRunner.Run("registry.timeout_kills_tool", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("cli-registry"))
                {
                    ToolRegistry registry = LoadFixture(ws);
                    string content;
                    int exitCode;
                    Stopwatch sw = Stopwatch.StartNew();
                    registry.ExecuteToolCall("fix_hang", "{}", out content, out exitCode);
                    sw.Stop();
                    TestAssert.Equal(-1, exitCode, "exit");
                    TestAssert.Contains(content, "timed out after 1 second(s)", "message");
                    TestAssert.True(sw.ElapsedMilliseconds < 6000, "returned promptly ms=" + sw.ElapsedMilliseconds);
                }
            });

            TestRunner.Run("registry.context_injection", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("cli-registry"))
                {
                    ToolRegistry registry = LoadFixture(ws);

                    List<string> injections = registry.GetContextInjections(new List<string> { "fix_echo", "fix_fail" });
                    TestAssert.Equal(1, injections.Count, "one per package");
                    TestAssert.Equal("injected-context", injections[0], "trimmed text");

                    TestAssert.Equal(0, registry.GetContextInjections(new List<string> { "other_tool" }).Count,
                        "package with no enabled tool is not asked");
                    TestAssert.Equal(0, registry.GetContextInjections(new List<string>()).Count, "no tools");
                    TestAssert.Equal(0, registry.GetContextInjections(null).Count, "null tools");
                }
            });
        }

        // ---- Cancel ----

        /// <summary>Sets the stop event the GUI uses, for this process.</summary>
        private static void SignalStop()
        {
            if (!_listening)
            {
                Cancel.Listen();
                _listening = true;
            }

            using (EventWaitHandle stop = EventWaitHandle.OpenExisting(
                ChatPipe.GetCancelEventName(Process.GetCurrentProcess().Id)))
            {
                stop.Set();
            }
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        private static void ResetCancel()
        {
            Cancel.End();
            Cancel.Requested = false;
        }

        private static void CancelTests()
        {
            TestRunner.Run("cancel.stop_runs_registered_abort", () =>
            {
                try
                {
                    bool first = false;
                    bool second = false;
                    Cancel.Begin();
                    TestAssert.True(!Cancel.Requested, "a new turn starts clear");
                    Cancel.OnAbort(() => first = true);
                    Cancel.OnAbort(() => second = true);
                    SignalStop();
                    TestAssert.True(WaitUntil(() => second, 3000), "latest abort ran");
                    TestAssert.True(Cancel.Requested, "requested");
                    TestAssert.True(!first, "replaced abort did not run");

                    Cancel.End();
                    Cancel.Begin();
                    TestAssert.True(!Cancel.Requested, "the next turn starts clear");
                }
                finally
                {
                    ResetCancel();
                }
            });

            TestRunner.Run("cancel.stop_before_registration", () =>
            {
                try
                {
                    Cancel.Begin();
                    SignalStop();
                    TestAssert.True(WaitUntil(() => Cancel.Requested, 3000), "requested");

                    // The operation that starts after the stop is unblocked at once.
                    bool ran = false;
                    Cancel.OnAbort(() => ran = true);
                    TestAssert.True(ran, "late abort ran immediately");

                    // An abort that throws must not take the listener down.
                    Cancel.OnAbort(() => { throw new InvalidOperationException("abort failed"); });
                    ran = false;
                    Cancel.OnAbort(() => ran = true);
                    TestAssert.True(ran, "still working after a throwing abort");
                }
                finally
                {
                    ResetCancel();
                }
            });

            TestRunner.Run("cancel.ignored_outside_a_turn", () =>
            {
                try
                {
                    ResetCancel();
                    bool ran = false;
                    Cancel.OnAbort(() => ran = true);
                    SignalStop();
                    TestAssert.True(WaitUntil(() => Cancel.Requested, 3000), "stop seen");
                    TestAssert.True(!ran, "nothing registered outside a turn");
                }
                finally
                {
                    ResetCancel();
                }
            });

            TestRunner.Run("cancel.stop_kills_running_tool", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("cli-registry"))
                {
                    ToolRegistry registry = LoadFixture(ws);
                    try
                    {
                        string content = null;
                        int exitCode = 0;
                        Cancel.Begin();
                        // fix_spin has no timeout: only the stop can end it.
                        Thread worker = new Thread(() =>
                            registry.ExecuteToolCall("fix_spin", "{}", out content, out exitCode));
                        worker.IsBackground = true;
                        worker.Start();

                        Thread.Sleep(500);
                        TestAssert.True(worker.IsAlive, "tool still running before the stop");
                        SignalStop();
                        TestAssert.True(worker.Join(8000), "tool call returned after the stop");
                        TestAssert.Equal(-1, exitCode, "exit");
                        TestAssert.True(Cancel.Requested, "requested");
                    }
                    finally
                    {
                        ResetCancel();
                    }
                }
            });
        }
    }
}
