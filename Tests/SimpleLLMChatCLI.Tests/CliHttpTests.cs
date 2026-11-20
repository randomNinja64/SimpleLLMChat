using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace SimpleLLMChatCLI.Tests
{
    /// <summary>
    /// Black-box tests of what the packaged CLI sends, how it reports HTTP
    /// failures, and how it stops a response.
    /// </summary>
    internal static class CliHttpTests
    {
        public static void Run()
        {
            TestRunner.Run("cli.request_payload_tool_loop", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = Program.PackageCliWorkspace())
                {
                    if (!Program.ShellToolsPackaged(ws))
                        TestRunner.Skip("tools/ShellTools not packaged beside CLI test host");

                    const string arguments = "{\"command\":\"echo PAYLOAD_OK\"}";
                    server.StreamDelayMs = 0;
                    server.EnqueueToolCall("run_shell_command", "call_payload", arguments);
                    server.EnqueueContent("payload-final");
                    Program.WriteCliIni(ws.Path, server.BaseUrl + "/v1", "run_shell_command", "");

                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners --reasoning-effort high please run",
                        ws.Path, null, 30000);
                    TestAssert.Contains(r.Stdout, "payload-final", "final");
                    TestAssert.Equal("Bearer test-key", server.LastAuthorization, "api key");

                    List<string> bodies = server.ChatBodies;
                    TestAssert.Equal(2, bodies.Count, "two requests");

                    JObject first = JObject.Parse(bodies[0]);
                    TestAssert.Equal("test-model", (string)first["model"], "model");
                    TestAssert.True((bool)first["stream"], "stream");
                    TestAssert.Equal("high", (string)first["reasoning_effort"], "reasoning effort");
                    JArray messages = (JArray)first["messages"];
                    TestAssert.Equal(2, messages.Count, "system and user");
                    TestAssert.Equal("system", (string)messages[0]["role"], "system role");
                    TestAssert.Contains((string)messages[0]["content"], "You are a test assistant.", "system prompt");
                    TestAssert.Equal("user", (string)messages[1]["role"], "user role");
                    TestAssert.Equal("please run", (string)messages[1]["content"], "user text");
                    JArray tools = (JArray)first["tools"];
                    TestAssert.Equal(1, tools.Count, "only enabled tools are offered");
                    TestAssert.Equal("run_shell_command", (string)tools[0]["function"]["name"], "tool name");

                    JObject second = JObject.Parse(bodies[1]);
                    messages = (JArray)second["messages"];
                    TestAssert.Equal(4, messages.Count, "call and result appended");
                    JObject assistant = (JObject)messages[2];
                    TestAssert.Equal("assistant", (string)assistant["role"], "assistant role");
                    JObject call = (JObject)assistant["tool_calls"][0];
                    TestAssert.Equal("call_payload", (string)call["id"], "call id");
                    TestAssert.Equal("function", (string)call["type"], "call type");
                    TestAssert.Equal("run_shell_command", (string)call["function"]["name"], "call name");
                    TestAssert.Equal(arguments, (string)call["function"]["arguments"], "arguments sent back as a string");
                    JObject result = (JObject)messages[3];
                    TestAssert.Equal("tool", (string)result["role"], "tool role");
                    TestAssert.Equal("call_payload", (string)result["tool_call_id"], "result tied to the call");
                    TestAssert.Contains((string)result["content"], "Exit Code: 0", "result exit code");
                    TestAssert.ContainsIgnoreCase((string)result["content"], "PAYLOAD_OK", "result output");
                }
            });

            TestRunner.Run("cli.request_payload_image", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = Program.PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueContent("image-seen");
                    Program.WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");
                    string imagePath = Path.Combine(ws.Path, "tiny.png");
                    File.WriteAllBytes(imagePath, new byte[] { 1, 2, 3, 4 });

                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners --image \"" + imagePath + "\" describe this",
                        ws.Path, null, 20000);
                    TestAssert.Contains(r.Stdout, "image-seen", "reply");

                    JObject body = JObject.Parse(server.ChatBodies[0]);
                    TestAssert.True(body["tools"] == null, "no tools key when none are enabled");
                    TestAssert.True(body["reasoning_effort"] == null, "no reasoning effort unless asked");
                    JArray parts = (JArray)body["messages"][1]["content"];
                    TestAssert.Equal(2, parts.Count, "text and image parts");
                    TestAssert.Equal("text", (string)parts[0]["type"], "text part");
                    TestAssert.Equal("describe this", (string)parts[0]["text"], "text");
                    TestAssert.Equal("image_url", (string)parts[1]["type"], "image part");
                    TestAssert.Equal("data:image/png;base64,AQIDBA==", (string)parts[1]["image_url"]["url"], "data URL");
                }
            });

            TestRunner.Run("cli.http_error_then_recover", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = Program.PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueHttpError(401, "{\"error\":{\"message\":\"bad-key-marker\"}}");
                    server.EnqueueContent("recovered-ok");
                    Program.WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"), "--no-banners", ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("hello");
                        TestAssert.True(
                            Program.WaitStdoutContains(cli, "Request to LLM Failed (HTTP 401", 20000), "status shown");
                        TestAssert.True(Program.WaitStdoutContains(cli, "bad-key-marker", 5000), "server's message shown");

                        cli.WriteLine("again");
                        TestAssert.True(Program.WaitStdoutContains(cli, "recovered-ok", 20000), "next turn works");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("cli.server_unreachable", () =>
            {
                // A port that was just listening and no longer is.
                string deadUrl;
                using (FakeOpenAiServer closed = new FakeOpenAiServer())
                    deadUrl = closed.BaseUrl;

                using (TempWorkspace ws = Program.PackageCliWorkspace())
                {
                    Program.WriteCliIni(ws.Path, deadUrl + "/v1", "", "");
                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "-o --no-banners ping",
                        ws.Path, null, 30000);
                    TestAssert.True(!r.TimedOut, "did not hang");
                    TestAssert.Contains(r.Stdout, "Request to LLM Failed (", "failure shown in output-only mode");
                }
            });

            TestRunner.Run("cli.stream_dropped_then_recover", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = Program.PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueTruncatedContent("partial-before-drop");
                    server.EnqueueContent("after-drop-ok");
                    Program.WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"), "--no-banners", ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("hello");
                        TestAssert.True(Program.WaitStdoutContains(cli, "partial-before-drop", 20000), "partial text shown");
                        TestAssert.True(Program.WaitStdoutContains(cli, "Request to LLM Failed (", 20000), "drop reported");

                        cli.WriteLine("again");
                        TestAssert.True(Program.WaitStdoutContains(cli, "after-drop-ok", 20000), "next turn works");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("cli.stop_mid_stream", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = Program.PackageCliWorkspace())
                {
                    // About 8 seconds of streaming unless it is stopped.
                    StringBuilder slow = new StringBuilder("STOPSTART-");
                    for (int i = 0; i < 75; i++)
                        slow.Append("abc ");
                    slow.Append("STOP-TAIL-MARKER");
                    server.StreamDelayMs = 100;
                    server.StreamChunkChars = 4;
                    server.EnqueueContent(slow.ToString());
                    Program.WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"), "--no-banners", ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("go");
                        TestAssert.True(Program.WaitStdoutContains(cli, "STOPSTART-", 20000), "stream started");

                        // What the GUI's Stop button does.
                        using (EventWaitHandle stop = EventWaitHandle.OpenExisting(ChatPipe.GetCancelEventName(cli.Id)))
                            stop.Set();
                        TestAssert.True(Program.WaitStdoutContains(cli, "[Stopped]", 5000), "stopped promptly");
                        TestAssert.True(
                            cli.Stdout.IndexOf("STOP-TAIL-MARKER", StringComparison.Ordinal) < 0,
                            "rest of the reply never arrived");

                        server.EnqueueContent("after-stop-ok");
                        cli.WriteLine("next");
                        TestAssert.True(Program.WaitStdoutContains(cli, "after-stop-ok", 30000), "next turn works");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");

                        // The partial reply stays in the conversation.
                        List<string> bodies = server.ChatBodies;
                        JArray messages = (JArray)JObject.Parse(bodies[bodies.Count - 1])["messages"];
                        TestAssert.Equal(4, messages.Count, "system, user, partial reply, user");
                        TestAssert.Equal("assistant", (string)messages[2]["role"], "partial reply role");
                        string partial = (string)messages[2]["content"];
                        TestAssert.True(partial.StartsWith("STOPSTART-", StringComparison.Ordinal), "partial reply kept");
                        TestAssert.True(partial.IndexOf("STOP-TAIL-MARKER", StringComparison.Ordinal) < 0, "partial reply is partial");
                        TestAssert.Equal("next", (string)messages[3]["content"], "new message");
                    }
                }
            });
        }
    }
}
