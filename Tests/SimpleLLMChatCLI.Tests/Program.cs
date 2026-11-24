using Newtonsoft.Json.Linq;
using SimpleLLMChatCLI;
using SimpleLLMChatCLI.RAG;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SimpleLLMChatCLI.Tests
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            return SuiteMain.Run("SimpleLLMChatCLI", args, Run);
        }

        static void Run()
        {
            // In-process tests of the linked CLI sources.
            StreamingTests.Run();
            ToolRegistryTests.Run();
            RagTests.Run();

            TestRunner.Run("TokenEstimator.basic", () =>
            {
                TestAssert.Equal(0, TokenEstimator.ApproximateTokens(0), "zero");
                TestAssert.Equal(3, TokenEstimator.ApproximateTokens(10), "10/3");
                TestAssert.True(TokenEstimator.ShouldSummarize(3000, 1000), "summarize");
                TestAssert.True(!TokenEstimator.ShouldSummarize(100, 1000), "no summarize");
            });

            TestRunner.Run("ConsecutiveToolFailures.streak", () =>
            {
                int streak = 0;

                ConsecutiveToolFailures.Record(ref streak, 0, true, 1);
                TestAssert.Equal(0, streak, "limit 0 ignores failure");

                ConsecutiveToolFailures.Record(ref streak, 3, true, 1);
                ConsecutiveToolFailures.Record(ref streak, 3, true, 1);
                TestAssert.Equal(2, streak, "two failures");

                ConsecutiveToolFailures.Record(ref streak, 3, false, 1);
                TestAssert.Equal(2, streak, "non-run leaves streak");

                ConsecutiveToolFailures.Record(ref streak, 3, true, 0);
                TestAssert.Equal(0, streak, "success resets");

                ConsecutiveToolFailures.Record(ref streak, 3, true, 1);
                ConsecutiveToolFailures.Record(ref streak, 3, true, 2);
                ConsecutiveToolFailures.Record(ref streak, 3, true, -1);
                TestAssert.Equal(3, streak, "three failures");

                ConsecutiveToolFailures.Record(ref streak, 3, true, 0);
                TestAssert.Equal(0, streak, "later success clears a reached limit");
            });

            TestRunner.Run("ToolApproval.format", () =>
            {
                TestAssert.Equal(
                    "Run tool: read_file\n{\"filename\":\"a.txt\"}",
                    ToolApproval.FormatApprovalMessage("read_file", "{\\\"filename\\\":\\\"a.txt\\\"}"),
                    "escaped arguments shown unescaped");
                TestAssert.Equal(
                    "Run tool: run_shell_command\nJev confidence: 0.91\n{\"command\":\"echo hi\"}",
                    ToolApproval.FormatApprovalMessage("run_shell_command", "{\"command\":\"echo hi\"}", "0.91"),
                    "confidence line");
            });

            TestRunner.Run("JevDecisionClient.parse", () =>
            {
                JevGateDecision allow = JevDecisionClient.ParseResponse(
                    "{\"answers\":{\"gate\":{\"type\":\"choice\",\"choice\":\"allow\",\"confidence\":0.91}}}");
                TestAssert.Equal("Allow", allow.Choice.ToString(), "allow");
                TestAssert.True(allow.Confidence.HasValue, "allow confidence present");
                TestAssert.Equal("0.91", allow.Confidence.Value.ToString("0.00", CultureInfo.InvariantCulture), "allow confidence");

                JevGateDecision confirm = JevDecisionClient.ParseResponse(
                    "{\"answers\":{\"gate\":{\"choice\":\"confirm\",\"confidence\":0.42}}}");
                TestAssert.Equal("Confirm", confirm.Choice.ToString(), "confirm");
                TestAssert.Equal("0.42", confirm.Confidence.Value.ToString("0.00", CultureInfo.InvariantCulture), "confirm confidence");

                JevGateDecision missing = JevDecisionClient.ParseResponse("{\"model\":\"jev-latest\"}");
                TestAssert.Equal("Confirm", missing.Choice.ToString(), "missing");
                TestAssert.True(!missing.Confidence.HasValue, "missing confidence");

                JevGateDecision error = JevDecisionClient.ParseResponse("{\"error\":{\"message\":\"nope\"}}");
                TestAssert.Equal("Confirm", error.Choice.ToString(), "error body");
                TestAssert.True(!error.Confidence.HasValue, "error confidence");

                var messages = new List<JevContextMessage>();
                messages.Add(new JevContextMessage { Role = "user", Text = "too old" });
                messages.Add(new JevContextMessage { Role = "user", Text = "please read" });
                messages.Add(new JevContextMessage { Role = "assistant", Text = "reading" });
                messages.Add(new JevContextMessage { Role = "tool", Text = new string('x', 600) + "TAIL" });
                string request = JevDecisionClient.BuildRequestJson(
                    "", "read_file", "{}", "Read a file from disk", messages);
                TestAssert.True(request.IndexOf("\"model\"", StringComparison.Ordinal) < 0, "model omitted");
                TestAssert.Contains(request, "\"allow\"", "allow criterion");
                TestAssert.Contains(request, "\"confirm\"", "confirm criterion");
                TestAssert.Contains(request, "Read a file from disk", "description");
                TestAssert.Contains(request, "please read", "recent user");
                TestAssert.True(request.IndexOf("too old", StringComparison.Ordinal) < 0, "older than 3 dropped");
                TestAssert.True(request.IndexOf("TAIL", StringComparison.Ordinal) < 0, "long message truncated");
                TestAssert.Contains(request, "...", "truncation mark");
            });

            TestRunner.Run("ToolResultParser.json", () =>
            {
                string text, image, mime;
                ToolResultParser.Parse("{\"text\":\"hi\",\"image\":{\"data\":\"QQ==\",\"mime\":\"image/png\"}}",
                    out text, out image, out mime);
                TestAssert.Equal("hi", text, "text");
                TestAssert.Equal("QQ==", image, "image");
                TestAssert.Equal("image/png", mime, "mime");

                ToolResultParser.Parse(
                    "{\"text\":\"captured\",\"image\":{\"data\":\"QQ==\",\"mime\":\"image/jpeg\"}}",
                    "curl: (23) Failed writing body\n",
                    out text, out image, out mime);
                TestAssert.Equal("captured\ncurl: (23) Failed writing body\n", text, "stderr kept as text");
                TestAssert.Equal("QQ==", image, "image kept");
                TestAssert.Equal("image/jpeg", mime, "mime kept");
            });

            TestRunner.Run("VectorStore.search", () =>
            {
                VectorStore store = new VectorStore();
                store.Add(new ChunkVector
                {
                    File = "a.txt",
                    StartLine = 1,
                    EndLine = 2,
                    Embedding = new float[] { 1f, 0f, 0f }
                });
                store.Add(new ChunkVector
                {
                    File = "b.txt",
                    StartLine = 1,
                    EndLine = 2,
                    Embedding = new float[] { 0f, 1f, 0f }
                });
                List<ChunkHit> hits = store.Search(new float[] { 1f, 0f, 0f }, 1);
                TestAssert.Equal(1, hits.Count, "count");
                TestAssert.Equal("a.txt", hits[0].Chunk.File, "top file");
            });

            TestRunner.Run("Ini.ConfigHandler", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("cli-ini"))
                {
                    string path = ws.Combine("LLMSettings.ini");
                    File.WriteAllText(path,
                        "[System]\r\napikey=k\r\nllmserver=http://127.0.0.1\r\nmodel=m\r\n",
                        Encoding.UTF8);
                    ConfigHandler cfg = new ConfigHandler(path);
                    TestAssert.Equal("k", cfg.GetConfigValue("apikey"), "apikey");
                    TestAssert.Equal("m", cfg.GetConfigValue("model"), "model");
                }
            });

            TestRunner.Run("ImageEncoder.mime", () =>
            {
                TestAssert.Equal("image/jpeg", ImageEncoder.GuessMime("photo.JPG"), "jpeg");
                TestAssert.Equal("image/jpeg", ImageEncoder.GuessMime("x.jpeg"), "jpeg long");
                TestAssert.Equal("image/png", ImageEncoder.GuessMime("shot.PNG"), "png");
                TestAssert.Equal("image/gif", ImageEncoder.GuessMime("a.gif"), "gif");
                TestAssert.Equal("image/webp", ImageEncoder.GuessMime("a.webp"), "webp");
                TestAssert.Equal("image/png", ImageEncoder.GuessMime("unknown.bin"), "default");
                TestAssert.Equal("image/png", ImageEncoder.GuessMime(null), "null");
            });

            TestRunner.Run("cli.bad_reasoning_effort", () =>
            {
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--reasoning-effort nope",
                        ws.Path, null, 15000);
                    TestAssert.ContainsIgnoreCase(r.Stderr + r.Stdout, "Invalid reasoning effort", "error");
                }
            });

            TestRunner.Run("cli.missing_image_path", () =>
            {
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--image",
                        ws.Path, null, 15000);
                    TestAssert.ContainsIgnoreCase(r.Stderr + r.Stdout, "requires a file path", "error");
                }
            });

            TestRunner.Run("cli.fake_openai_output_only", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.FixedReply = "canned-reply-ok";
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");
                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "-o --no-banners ping",
                        ws.Path, null, 20000);
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Contains(r.Stdout, "canned-reply-ok", "reply");
                }
            });

            TestRunner.Run("cli.llmserver_trailing_slash", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.FixedReply = "slash-reply-ok";
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1/", "", "");
                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "-o --no-banners ping",
                        ws.Path, null, 20000);
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Contains(r.Stdout, "slash-reply-ok", "reply");
                }
            });

            TestRunner.Run("cli.sse_error_field", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.StreamChunkChars = 16;
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");
                    string exe = Path.Combine(ws.Path, "SimpleLLMChatCLI.exe");

                    server.EnqueueContent("error");
                    ProcessResult word = ProcessRunner.Run(exe, "-o --no-banners ping", ws.Path, null, 20000);
                    TestAssert.Equal(0, word.ExitCode, "word exit");
                    TestAssert.Contains(word.Stdout, "error", "word kept");
                    TestAssert.True(word.Stdout.IndexOf("[API Error]", StringComparison.Ordinal) < 0, "word not an api error");

                    server.EnqueueContent("an error occurred");
                    ProcessResult phrase = ProcessRunner.Run(exe, "-o --no-banners ping", ws.Path, null, 20000);
                    TestAssert.Equal(0, phrase.ExitCode, "phrase exit");
                    TestAssert.Contains(phrase.Stdout, "an error occurred", "phrase kept");
                    TestAssert.True(phrase.Stdout.IndexOf("[API Error]", StringComparison.Ordinal) < 0, "phrase not an api error");

                    server.EnqueueContentWithNullError("beside-null");
                    ProcessResult nulled = ProcessRunner.Run(exe, "-o --no-banners ping", ws.Path, null, 20000);
                    TestAssert.Equal(0, nulled.ExitCode, "null error exit");
                    TestAssert.Contains(nulled.Stdout, "beside-null", "null error still streams");
                    TestAssert.True(nulled.Stdout.IndexOf("[API Error]", StringComparison.Ordinal) < 0, "null error not an api error");

                    server.EnqueueApiError("nope");
                    ProcessResult failed = ProcessRunner.Run(exe, "-o --no-banners ping", ws.Path, null, 20000);
                    TestAssert.Contains(failed.Stdout, "[API Error]", "api error shown");
                    TestAssert.Contains(failed.Stdout, "nope", "api error body");
                }
            });

            TestRunner.Run("cli.summary_failure_drops_old_messages", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueContent("first-reply");
                    server.EnqueueContent("second-reply");
                    server.EnqueueApiError("summary-down");
                    server.EnqueueContent("after-trim");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "", null, null, null, null, null, 200);
                    string oldMarker = new string('A', 400);
                    string keptMarker = new string('B', 200);

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "startup ready");
                        cli.WriteLine(oldMarker);
                        TestAssert.True(WaitStdoutContains(cli, "first-reply", 20000), "first reply");

                        cli.WriteLine(keptMarker);
                        TestAssert.True(WaitStdoutContains(cli, "second-reply", 20000), "second reply");

                        cli.WriteLine("please trim");
                        TestAssert.True(WaitStdoutContains(cli, "after-trim", 20000), "follow-up");
                        TestAssert.True(WaitStdoutContains(cli, "[Summarization failed - dropped older messages]", 5000), "dropped note");
                        TestAssert.True(
                            cli.Stdout.IndexOf("current turn still exceeds", StringComparison.Ordinal) < 0,
                            "current turn fits");

                        int tokens = 0;
                        DateTime deadline = DateTime.UtcNow.AddMilliseconds(5000);
                        while (DateTime.UtcNow < deadline)
                        {
                            tokens = probe.LastTokens();
                            if (tokens > 0 && tokens < 180)
                                break;
                            System.Threading.Thread.Sleep(25);
                        }
                        TestAssert.True(tokens > 0 && tokens < 180, "tokens under window tokens=" + tokens);
                        TestAssert.True(server.LastBody.IndexOf(oldMarker, StringComparison.Ordinal) < 0, "old history omitted");
                        TestAssert.Contains(server.LastBody, keptMarker, "previous turn kept");
                        TestAssert.Contains(server.LastBody, "please trim", "new message kept");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("cli.rag_injects_whole_chunk", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueContent("rag-reply-ok");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");
                    // Later keys win: turn RAG on, embeddings on the same fake server.
                    File.AppendAllText(
                        Path.Combine(ws.Path, "LLMSettings.ini"),
                        "ragenabled=1\r\nembeddingsmodel=test-embed\r\n");

                    // One 30-line file is a single chunk under the default 60-line chunk size.
                    StringBuilder notes = new StringBuilder();
                    for (int i = 1; i <= 30; i++)
                        notes.Append("RAGLINE-" + i.ToString("00", CultureInfo.InvariantCulture) + "\r\n");
                    Directory.CreateDirectory(Path.Combine(ws.Path, "knowledge"));
                    File.WriteAllText(Path.Combine(ws.Path, "knowledge", "notes.txt"), notes.ToString(), Encoding.UTF8);

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("what do the notes say");
                        TestAssert.True(WaitStdoutContains(cli, "rag-reply-ok", 30000), "reply");
                        TestAssert.Contains(cli.Stdout, "[reading notes.txt]", "status line");
                        TestAssert.True(server.EmbeddingsRequestCount >= 2, "index and query embedded");

                        // The chat request carries the whole chunk, not just its opening lines.
                        string chatBody = server.LastBody;
                        TestAssert.Contains(chatBody, "Retrieved Context", "context block");
                        TestAssert.Contains(chatBody, "RAGLINE-01", "first line");
                        TestAssert.Contains(chatBody, "RAGLINE-11", "line past the old 10-line cut");
                        TestAssert.Contains(chatBody, "RAGLINE-30", "last line");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("cli.image_counts_toward_tokens", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueContent("seen");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");

                    // Big enough that its base64 length would read as far more than the flat estimate.
                    byte[] blob = new byte[60000];
                    for (int i = 0; i < blob.Length; i++)
                        blob[i] = (byte)(i % 256);
                    string imagePath = Path.Combine(ws.Path, "shot.png");
                    File.WriteAllBytes(imagePath, blob);
                    int minTokens = TokenEstimator.ImageTokenEstimate;
                    int maxTokens = TokenEstimator.ImageTokenEstimate + 500;

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("/image \"" + imagePath + "\" see");
                        TestAssert.True(WaitStdoutContains(cli, "seen", 20000), "reply");

                        int tokens = 0;
                        DateTime deadline = DateTime.UtcNow.AddMilliseconds(5000);
                        while (DateTime.UtcNow < deadline)
                        {
                            tokens = probe.LastTokens();
                            if (tokens >= minTokens)
                                break;
                            System.Threading.Thread.Sleep(25);
                        }
                        TestAssert.True(tokens >= minTokens, "image counted tokens=" + tokens + " min=" + minTokens);
                        TestAssert.True(tokens <= maxTokens, "image not counted by size tokens=" + tokens + " max=" + maxTokens);
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("ChatPipe.ready_parse", () =>
            {
                TestAssert.True(ChatPipe.TryParseReadyLine(ChatPipe.ReadyLine), "ready");
                TestAssert.True(!ChatPipe.TryParseReadyLine("STATUS tokens=1"), "not ready");
            });

            TestRunner.Run("ChatPipe.approval_roundtrip", () =>
            {
                string args = "{\"command\":\"echo hi\\nnext\"}";
                string line = ChatPipe.FormatApproval("run_shell_command", args);
                string name, parsed, confidence;
                TestAssert.True(ChatPipe.TryParseApprovalLine(line, out name, out parsed, out confidence), "parse");
                TestAssert.Equal("run_shell_command", name, "name");
                TestAssert.Equal(args, parsed, "args");
                TestAssert.True(string.IsNullOrEmpty(confidence), "no confidence");

                string withConfidence = ChatPipe.FormatApproval("run_shell_command", args, 0.91);
                TestAssert.True(ChatPipe.TryParseApprovalLine(withConfidence, out name, out parsed, out confidence), "parse confidence");
                TestAssert.Equal("0.91", confidence, "confidence");
                TestAssert.Equal(args, parsed, "args with confidence");
            });

            TestRunner.Run("cli.approval_denied_output_only", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_deny_o",
                        "{\"command\":\"echo SHOULD_NOT_RUN\"}");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "run_shell_command", "run_shell_command");
                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "-o --no-banners please run a command",
                        ws.Path, null, 20000);
                    TestAssert.Contains(r.Stdout, "requires approval", "error");
                    TestAssert.True(r.Stdout.IndexOf("SHOULD_NOT_RUN", StringComparison.Ordinal) < 0, "no echo");
                }
            });

            TestRunner.Run("cli.tool_call_disabled", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_disabled",
                        "{\"command\":\"echo DISABLED_PATH\"}");
                    server.EnqueueContent("disabled-path-final");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");
                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners call a tool",
                        ws.Path, null, 25000);
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.ContainsIgnoreCase(r.Stdout, "disabled by configuration", "disabled");
                    TestAssert.Contains(r.Stdout, "disabled-path-final", "final");
                }
            });

            TestRunner.Run("cli.save_json", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_save",
                        "{\"command\":\"echo hi\"}");
                    server.EnqueueContent("save-reply-ok");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");
                    string mdPath = Path.Combine(ws.Path, "my chat.json");
                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("hello save");
                        TestAssert.True(WaitStdoutContains(cli, "save-reply-ok", 20000), "reply");
                        cli.WriteLine("/save \"" + mdPath + "\"");
                        string md = "";
                        DateTime deadline = DateTime.UtcNow.AddMilliseconds(10000);
                        while (DateTime.UtcNow < deadline)
                        {
                            if (File.Exists(mdPath))
                            {
                                md = File.ReadAllText(mdPath, Encoding.UTF8);
                                if (md.IndexOf("save-reply-ok", StringComparison.Ordinal) >= 0)
                                    break;
                            }
                            System.Threading.Thread.Sleep(50);
                        }
                        JArray messages = JArray.Parse(md);
                        TestAssert.Equal(4, messages.Count, "user, tool call, tool result, reply");

                        TestAssert.Equal("user", (string)messages[0]["role"], "user role");
                        TestAssert.Contains((string)messages[0]["content"], "hello save", "user text");

                        TestAssert.Equal("assistant", (string)messages[1]["role"], "call role");
                        JObject call = (JObject)messages[1]["tool_calls"][0];
                        TestAssert.Equal("call_save", (string)call["id"], "call id");
                        TestAssert.Equal("run_shell_command", (string)call["name"], "call name");
                        TestAssert.Equal("{\"command\":\"echo hi\"}", (string)call["arguments"], "call arguments");

                        TestAssert.Equal("tool", (string)messages[2]["role"], "tool role");
                        TestAssert.Equal("call_save", (string)messages[2]["tool_call_id"], "result links to call");

                        TestAssert.Equal("assistant", (string)messages[3]["role"], "reply role");
                        TestAssert.Equal("save-reply-ok", (string)messages[3]["content"], "reply");
                        TestAssert.True(messages[3]["tool_calls"] == null, "plain reply has no tool calls");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("cli.load_json", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueContent("after-load-ok");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");
                    string chatPath = Path.Combine(ws.Path, "saved chat.json");
                    File.WriteAllText(chatPath, new JArray
                    {
                        new JObject { ["role"] = "user", ["content"] = "loaded-question" },
                        new JObject
                        {
                            ["role"] = "assistant",
                            ["content"] = "",
                            ["tool_calls"] = new JArray
                            {
                                new JObject { ["id"] = "call_old", ["name"] = "run_shell_command", ["arguments"] = "{\"command\":\"echo old\"}" }
                            }
                        },
                        new JObject { ["role"] = "tool", ["content"] = "Command: run_shell_command\nExit Code: 0\nOutput:\nloaded-tool-output", ["tool_call_id"] = "call_old" },
                        new JObject { ["role"] = "assistant", ["content"] = "loaded-answer" }
                    }.ToString(), Encoding.UTF8);

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("/load \"" + chatPath + "\"");
                        TestAssert.True(WaitStdoutContains(cli, "loaded-answer", 10000), "replayed reply");
                        TestAssert.Contains(cli.Stdout, "You: loaded-question", "replayed user");
                        // A turn that opens with a tool call prints the name on its own line.
                        TestAssert.Contains(cli.Stdout, "LLM:\n", "replayed assistant name");
                        TestAssert.Contains(cli.Stdout, "loaded-tool-output", "replayed tool output");

                        cli.WriteLine("next question");
                        TestAssert.True(WaitStdoutContains(cli, "after-load-ok", 20000), "reply");
                        JArray sent = (JArray)JObject.Parse(server.ChatBodies[0])["messages"];
                        string history = sent.ToString();
                        TestAssert.Contains(history, "loaded-question", "history user");
                        TestAssert.Contains(history, "call_old", "history tool call");
                        TestAssert.Contains(history, "loaded-answer", "history reply");
                        TestAssert.Contains((string)sent[sent.Count - 1]["content"], "next question", "new message last");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("cli.status_ready", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");
                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("cli.status_approval_deny", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_deny",
                        "{\"command\":\"echo APPROVAL_DENY_MARKER\"}");
                    server.EnqueueContent("deny-final-ok");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "run_shell_command", "run_shell_command");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("please run the command");
                        string tool, args;
                        TestAssert.True(probe.WaitForApproval(20000, out tool, out args, out _), "approval");
                        TestAssert.Equal("run_shell_command", tool, "tool");
                        cli.WriteLine("N");
                        TestAssert.True(WaitStdoutContains(cli, "deny-final-ok", 20000), "final");
                        string outText = cli.Stdout;
                        TestAssert.ContainsIgnoreCase(outText, "cancelled by the user", "cancelled");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("cli.approval_keeps_queued_command", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_queued",
                        "{\"command\":\"echo QUEUED_MARKER\"}");
                    server.EnqueueContent("queued-final-ok");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "run_shell_command", "run_shell_command");
                    string mdPath = Path.Combine(ws.Path, "queued.json");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("please run the command");
                        string tool, args;
                        TestAssert.True(probe.WaitForApproval(20000, out tool, out args, out _), "approval");
                        // A command lands on stdin ahead of the answer, as when the
                        // GUI's Save Chat is clicked mid-turn.
                        cli.WriteLine("/save \"" + mdPath + "\"");
                        cli.WriteLine("N");
                        TestAssert.True(WaitStdoutContains(cli, "queued-final-ok", 20000), "final");
                        TestAssert.True(
                            cli.Stdout.IndexOf("Please enter Y or N", StringComparison.Ordinal) < 0,
                            "command not read as an answer");

                        string md = "";
                        DateTime deadline = DateTime.UtcNow.AddMilliseconds(10000);
                        while (DateTime.UtcNow < deadline)
                        {
                            if (File.Exists(mdPath))
                            {
                                md = File.ReadAllText(mdPath, Encoding.UTF8);
                                if (md.IndexOf("queued-final-ok", StringComparison.Ordinal) >= 0)
                                    break;
                            }
                            System.Threading.Thread.Sleep(50);
                        }
                        TestAssert.Contains(md, "queued-final-ok", "save ran after the turn");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("cli.tool_loop_echo", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    if (!ShellToolsPackaged(ws))
                        TestRunner.Skip("tools/ShellTools not packaged beside CLI test host");

                    server.StreamDelayMs = 0;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_echo",
                        "{\"command\":\"echo TOOL_LOOP_OK\"}");
                    server.EnqueueContent("tool-loop-final");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "run_shell_command", "");
                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners run echo",
                        ws.Path, null, 30000);
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Contains(r.Stdout, "[tool call] run_shell_command", "tool call");
                    TestAssert.Contains(r.Stdout, "[tool output]", "tool output");
                    TestAssert.ContainsIgnoreCase(r.Stdout, "TOOL_LOOP_OK", "echo");
                    TestAssert.Contains(r.Stdout, "tool-loop-final", "final");
                    TestAssert.Equal(0, server.SystemOneRequestCount, "no systemone");
                }
            });

            TestRunner.Run("cli.status_approval_allow", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    if (!ShellToolsPackaged(ws))
                        TestRunner.Skip("tools/ShellTools not packaged beside CLI test host");

                    server.StreamDelayMs = 0;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_allow",
                        "{\"command\":\"echo APPROVAL_ALLOW_OK\"}");
                    server.EnqueueContent("allow-final-ok");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "run_shell_command", "run_shell_command");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("please run the command");
                        string tool, args;
                        TestAssert.True(probe.WaitForApproval(20000, out tool, out args, out _), "approval");
                        TestAssert.Equal("run_shell_command", tool, "tool");
                        cli.WriteLine("Y");
                        TestAssert.True(WaitStdoutContains(cli, "allow-final-ok", 25000), "final");
                        string allowOut = cli.Stdout;
                        TestAssert.Contains(allowOut, "[tool output]", "tool output");
                        TestAssert.ContainsIgnoreCase(allowOut, "APPROVAL_ALLOW_OK", "echo");
                        TestAssert.True(
                            allowOut.IndexOf("cancelled by the user", StringComparison.OrdinalIgnoreCase) < 0,
                            "not cancelled");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("config.per_tool_auto_approval", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("approval-ini"))
                {
                    string path = ws.Combine("LLMSettings.ini");

                    File.WriteAllText(path, "[Tools]\r\nautoapprovaltools=read_file\r\ntoolsrequiringapproval=run_shell_command\r\n");
                    TurnSettings perTool = TurnSettings.FromConfig(new ConfigHandler(path), false);
                    TestAssert.True(perTool.AutoApprovalSet.Contains("READ_FILE"), "listed tool is auto");
                    TestAssert.True(!perTool.AutoApprovalSet.Contains("run_shell_command"), "unlisted tool is not auto");
                }
            });

            TestRunner.Run("cli.per_tool_auto_approval_allow", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    if (!ShellToolsPackaged(ws))
                        TestRunner.Skip("tools/ShellTools not packaged beside CLI test host");

                    server.StreamDelayMs = 0;
                    server.SystemOneChoice = "allow";
                    server.SystemOneConfidence = 0.91;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_per_tool_auto",
                        "{\"command\":\"echo PER_TOOL_AUTO_OK\"}");
                    server.EnqueueContent("per-tool-auto-final");
                    // Also on the manual list: the per-tool Auto choice wins.
                    WriteCliIni(
                        ws.Path,
                        server.BaseUrl + "/v1",
                        "run_shell_command",
                        "run_shell_command",
                        "run_shell_command",
                        server.BaseUrl + "/v1/systemone",
                        "jev-secret",
                        "");

                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners please run the command",
                        ws.Path, null, 30000);
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Contains(r.Stdout, "[run_shell_command auto allowed, confidence 0.91]", "auto line");
                    TestAssert.ContainsIgnoreCase(r.Stdout, "PER_TOOL_AUTO_OK", "echo");
                    TestAssert.True(r.Stdout.IndexOf("Approve? (Y/N):", StringComparison.Ordinal) < 0, "no prompt");
                    TestAssert.True(server.SystemOneRequestCount >= 1, "systemone called");
                }
            });

            TestRunner.Run("cli.auto_approval_allow", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    if (!ShellToolsPackaged(ws))
                        TestRunner.Skip("tools/ShellTools not packaged beside CLI test host");

                    server.StreamDelayMs = 0;
                    server.SystemOneChoice = "allow";
                    server.SystemOneConfidence = 0.91;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_auto_allow",
                        "{\"command\":\"echo AUTO_ALLOW_OK\"}");
                    server.EnqueueContent("auto-allow-final");
                    WriteCliIni(
                        ws.Path,
                        server.BaseUrl + "/v1",
                        "run_shell_command",
                        "",
                        "run_shell_command",
                        server.BaseUrl + "/v1/systemone",
                        "jev-secret",
                        "");

                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners please run the command",
                        ws.Path, null, 30000);
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Contains(r.Stdout, "[run_shell_command auto allowed, confidence 0.91]", "auto line");
                    TestAssert.ContainsIgnoreCase(r.Stdout, "AUTO_ALLOW_OK", "echo");
                    TestAssert.True(r.Stdout.IndexOf("Approve? (Y/N):", StringComparison.Ordinal) < 0, "no prompt");
                    TestAssert.True(server.SystemOneRequestCount >= 1, "systemone called");
                    TestAssert.Equal("Bearer jev-secret", server.SystemOneAuthorization, "bearer");
                    TestAssert.True(
                        server.SystemOneBody != null && server.SystemOneBody.IndexOf("\"model\"", StringComparison.Ordinal) < 0,
                        "model omitted");
                    TestAssert.Contains(server.SystemOneBody, "run_shell_command", "tool in state");
                }
            });

            TestRunner.Run("cli.auto_approval_allow_hides_confidence", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    if (!ShellToolsPackaged(ws))
                        TestRunner.Skip("tools/ShellTools not packaged beside CLI test host");

                    server.StreamDelayMs = 0;
                    server.SystemOneChoice = "allow";
                    server.SystemOneConfidence = 0.91;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_auto_allow_hide",
                        "{\"command\":\"echo AUTO_ALLOW_HIDE\"}");
                    server.EnqueueContent("auto-allow-hide-final");
                    WriteCliIni(
                        ws.Path,
                        server.BaseUrl + "/v1",
                        "run_shell_command",
                        "",
                        "run_shell_command",
                        server.BaseUrl + "/v1/systemone",
                        "jev-secret",
                        "",
                        false);

                    ProcessResult r = ProcessRunner.Run(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners please run the command",
                        ws.Path, null, 30000);
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.True(
                        r.Stdout.IndexOf("auto allowed", StringComparison.Ordinal) < 0,
                        "no auto line");
                    TestAssert.ContainsIgnoreCase(r.Stdout, "AUTO_ALLOW_HIDE", "echo");
                }
            });

            TestRunner.Run("cli.auto_approval_confirm", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.SystemOneChoice = "confirm";
                    server.SystemOneConfidence = 0.42;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_auto_confirm",
                        "{\"command\":\"echo AUTO_CONFIRM_SKIP\"}");
                    server.EnqueueContent("auto-confirm-final");
                    WriteCliIni(
                        ws.Path,
                        server.BaseUrl + "/v1",
                        "run_shell_command",
                        "",
                        "run_shell_command",
                        server.BaseUrl + "/v1/systemone",
                        "jev-secret",
                        "jev-latest");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("please run the command");
                        string tool, args, pipeConfidence;
                        TestAssert.True(probe.WaitForApproval(20000, out tool, out args, out pipeConfidence), "approval");
                        TestAssert.Equal("run_shell_command", tool, "tool");
                        TestAssert.Equal("0.42", pipeConfidence, "pipe confidence");
                        TestAssert.True(WaitStdoutContains(cli, "Jev confidence: 0.42", 5000), "confidence");
                        cli.WriteLine("N");
                        TestAssert.True(WaitStdoutContains(cli, "auto-confirm-final", 20000), "final");
                        TestAssert.ContainsIgnoreCase(cli.Stdout, "cancelled by the user", "cancelled");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("cli.auto_approval_confirm_hides_confidence", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.SystemOneChoice = "confirm";
                    server.SystemOneConfidence = 0.42;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_auto_confirm_hide",
                        "{\"command\":\"echo AUTO_CONFIRM_HIDE\"}");
                    server.EnqueueContent("auto-confirm-hide-final");
                    WriteCliIni(
                        ws.Path,
                        server.BaseUrl + "/v1",
                        "run_shell_command",
                        "",
                        "run_shell_command",
                        server.BaseUrl + "/v1/systemone",
                        "jev-secret",
                        "jev-latest",
                        false);

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("please run the command");
                        string tool, args, pipeConfidence;
                        TestAssert.True(probe.WaitForApproval(20000, out tool, out args, out pipeConfidence), "approval");
                        TestAssert.Equal("run_shell_command", tool, "tool");
                        TestAssert.True(string.IsNullOrEmpty(pipeConfidence), "pipe confidence hidden");
                        TestAssert.True(WaitStdoutContains(cli, "Approve? (Y/N):", 5000), "prompt");
                        TestAssert.True(
                            cli.Stdout.IndexOf("Jev confidence:", StringComparison.Ordinal) < 0,
                            "no confidence");
                        cli.WriteLine("N");
                        TestAssert.True(WaitStdoutContains(cli, "auto-confirm-hide-final", 20000), "final");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("cli.auto_approval_http_error", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.SystemOneStatusCode = 500;
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_auto_error",
                        "{\"command\":\"echo AUTO_ERROR_SKIP\"}");
                    server.EnqueueContent("auto-error-final");
                    WriteCliIni(
                        ws.Path,
                        server.BaseUrl + "/v1",
                        "run_shell_command",
                        "",
                        "run_shell_command",
                        server.BaseUrl + "/v1/systemone",
                        "jev-secret",
                        "");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("please run the command");
                        string tool, args;
                        TestAssert.True(probe.WaitForApproval(20000, out tool, out args, out _), "approval");
                        TestAssert.True(WaitStdoutContains(cli, "Approve? (Y/N):", 5000), "prompt");
                        TestAssert.True(
                            cli.Stdout.IndexOf("Jev confidence:", StringComparison.Ordinal) < 0,
                            "no confidence");
                        cli.WriteLine("N");
                        TestAssert.True(WaitStdoutContains(cli, "auto-error-final", 20000), "final");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");
                    }
                }
            });

            TestRunner.Run("cli.pipe_text_omits_you_prompt", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueContent("dialogue:\nYou: from-model");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"), "--no-banners", ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("hello");
                        TestAssert.True(WaitStdoutContains(cli, "from-model", 20000), "reply");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");

                        // The terminal gets the prompt; the pipe only the model's own "You:".
                        TestAssert.True(cli.Stdout.StartsWith("You: ", StringComparison.Ordinal), "prompt on stdout");
                        string chat = probe.ChatText();
                        TestAssert.Contains(chat, "dialogue:\nYou: from-model", "model line on pipe");
                        TestAssert.Equal(
                            chat.IndexOf("You:", StringComparison.Ordinal),
                            chat.LastIndexOf("You:", StringComparison.Ordinal),
                            "no prompt on pipe");
                    }
                }
            });

            TestRunner.Run("cli.pipe_tool_blocks_and_no_approval_prompt", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    if (!ShellToolsPackaged(ws))
                        TestRunner.Skip("tools/ShellTools not packaged beside CLI test host");

                    server.StreamDelayMs = 0;
                    // First call runs unprompted, second needs approval.
                    server.EnqueueToolCall(
                        "run_shell_command",
                        "call_pipe_block",
                        "{\"command\":\"echo PIPE_BLOCK_OK\"}");
                    server.EnqueueContent("pipe-block-final");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "run_shell_command", "");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"), "--no-banners", ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("please run the command");
                        TestAssert.True(WaitStdoutContains(cli, "pipe-block-final", 20000), "final");

                        // Now require approval and run it again.
                        server.EnqueueToolCall(
                            "run_shell_command",
                            "call_pipe_approve",
                            "{\"command\":\"echo PIPE_APPROVE_OK\"}");
                        server.EnqueueContent("pipe-approve-final");
                        File.AppendAllText(
                            Path.Combine(ws.Path, "LLMSettings.ini"),
                            "\r\ntoolsrequiringapproval=run_shell_command\r\n");
                        cli.WriteLine("/reload");
                        cli.WriteLine("again please");
                        string tool, args;
                        TestAssert.True(probe.WaitForApproval(20000, out tool, out args, out _), "approval on pipe");
                        TestAssert.True(WaitStdoutContains(cli, "Approve? (Y/N): ", 5000), "prompt on stdout");
                        cli.WriteLine("Y");
                        TestAssert.True(WaitStdoutContains(cli, "pipe-approve-final", 20000), "approved final");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");

                        // Terminal keeps the tags.
                        TestAssert.Contains(cli.Stdout, "[tool call] run_shell_command", "stdout tool call tag");
                        TestAssert.Contains(cli.Stdout, "[/tool output]", "stdout tool output tag");

                        // Pipe carries blocks as events and neither tags nor prompts as text.
                        string events = probe.ChatEvents();
                        TestLog.Detail("chat events:" + events);
                        TestAssert.Contains(events, "\nblock ToolCall run_shell_command\ntext {\"command\":\"echo PIPE_BLOCK_OK\"}", "tool call block");
                        TestAssert.Contains(events, "\nblock ToolOutput \ntext ", "tool output block");
                        TestAssert.Contains(events, "\nendblock\n", "block end");
                        string chat = probe.ChatText();
                        TestAssert.ContainsIgnoreCase(chat, "PIPE_APPROVE_OK", "approved tool output on pipe");
                        TestAssert.Contains(chat, "Run tool: run_shell_command", "approval message on pipe");
                        TestAssert.True(chat.IndexOf("Approve?", StringComparison.Ordinal) < 0, "no approval prompt on pipe");
                        TestAssert.True(chat.IndexOf("[tool", StringComparison.Ordinal) < 0, "no open tags on pipe");
                        TestAssert.True(chat.IndexOf("[/tool", StringComparison.Ordinal) < 0, "no close tags on pipe");
                    }
                }
            });

            TestRunner.Run("cli.pipe_inline_think_block", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    // 16-char stream chunks: "<think>pondering" then "</think>answer".
                    server.EnqueueContent("<think>pondering</think>answer");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"), "--no-banners", ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("hello");
                        TestAssert.True(WaitStdoutContains(cli, "answer", 20000), "reply");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");

                        TestAssert.Contains(cli.Stdout, "<think>pondering</think>answer", "stdout unchanged");
                        string events = probe.ChatEvents();
                        TestLog.Detail("chat events:" + events);
                        TestAssert.Contains(events, "\nblock Thinking \ntext pondering\nendblock\ntext answer", "thinking block");
                    }
                }
            });

            TestRunner.Run("cli.pipe_reasoning_field_block", () =>
            {
                using (FakeOpenAiServer server = new FakeOpenAiServer())
                using (TempWorkspace ws = PackageCliWorkspace())
                {
                    server.StreamDelayMs = 0;
                    server.EnqueueReasoningThenContent("mulling it over", "reasoned-answer");
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"), "--no-banners", ws.Path))
                    using (ChatPipeProbe probe = new ChatPipeProbe(cli.Id))
                    {
                        probe.Start();
                        TestAssert.True(probe.WaitForReady(15000), "ready");
                        cli.WriteLine("hello");
                        TestAssert.True(WaitStdoutContains(cli, "reasoned-answer", 20000), "reply");
                        cli.WriteLine("/exit");
                        TestAssert.True(cli.WaitForExit(10000), "exit");

                        TestAssert.Contains(
                            cli.Stdout.Replace("\r\n", "\n"),
                            "[thinking]\nmulling it over\n[/thinking]\n",
                            "stdout tags");
                        string events = probe.ChatEvents();
                        TestLog.Detail("chat events:" + events);
                        TestAssert.Contains(
                            events, "\nblock Thinking \ntext mulling it over\\n\nendblock\ntext ", "thinking block");
                        string chat = probe.ChatText();
                        TestAssert.Contains(chat, "reasoned-answer", "answer on pipe");
                        TestAssert.True(chat.IndexOf("thinking]", StringComparison.Ordinal) < 0, "no tags on pipe");
                    }
                }
            });

            // More black-box tests against the packaged CLI.
            CliHttpTests.Run();
        }

        internal static TempWorkspace PackageCliWorkspace()
        {
            TempWorkspace ws = new TempWorkspace("cli-run");
            string srcExe = ToolClient.ProductExe("SimpleLLMChatCLI.exe");
            string srcJson = ToolClient.ProductExe("Newtonsoft.Json.dll");
            if (!File.Exists(srcExe))
                throw new TestFailureException("SimpleLLMChatCLI.exe not packaged: " + srcExe);
            File.Copy(srcExe, Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"), true);
            if (File.Exists(srcJson))
                File.Copy(srcJson, Path.Combine(ws.Path, "Newtonsoft.Json.dll"), true);

            string toolsSrc = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
            if (Directory.Exists(toolsSrc))
                CopyDirectory(toolsSrc, Path.Combine(ws.Path, "tools"));

            return ws;
        }

        static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            foreach (string sub in Directory.GetDirectories(sourceDir))
                CopyDirectory(sub, Path.Combine(destDir, Path.GetFileName(sub)));
        }

        internal static bool ShellToolsPackaged(TempWorkspace ws)
        {
            return File.Exists(Path.Combine(ws.Path, "tools", "ShellTools", "ShellTools.exe"));
        }

        internal static void WriteCliIni(string dir, string llmServer, string tools, string toolsRequiringApproval)
        {
            WriteCliIni(dir, llmServer, tools, toolsRequiringApproval, null, null, null, null, null);
        }

        static void WriteCliIni(
            string dir,
            string llmServer,
            string tools,
            string toolsRequiringApproval,
            string autoApprovalTools,
            string jevBaseUrl,
            string jevApiKey,
            string jevModel,
            bool? showJevConfidence = null,
            int contextWindowSize = 0)
        {
            string confidenceLine = showJevConfidence.HasValue
                ? "showjevconfidence=" + (showJevConfidence.Value ? "1" : "0") + "\r\n"
                : "";
            string path = Path.Combine(dir, "LLMSettings.ini");
            File.WriteAllText(path,
                "[System]\r\n" +
                "apikey=test-key\r\n" +
                "llmserver=" + llmServer + "\r\n" +
                "model=test-model\r\n" +
                "sysprompt=\"You are a test assistant.\"\r\n" +
                "contextWindowSize=" + contextWindowSize.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "[Tools]\r\n" +
                "tools=" + (tools ?? "") + "\r\n" +
                "toolsrequiringapproval=" + (toolsRequiringApproval ?? "") + "\r\n" +
                "autoapprovaltools=" + (autoApprovalTools ?? "") + "\r\n" +
                "jevbaseurl=" + (jevBaseUrl ?? "") + "\r\n" +
                "jevapikey=" + (jevApiKey ?? "") + "\r\n" +
                "jevmodel=" + (jevModel ?? "") + "\r\n" +
                confidenceLine +
                "[Appearance]\r\n" +
                "assistantname=LLM\r\n" +
                "markdownparsing=0\r\n" +
                "[RAG]\r\n" +
                "ragenabled=0\r\n",
                Encoding.UTF8);
        }

        internal static bool WaitStdoutContains(CliProcess cli, string needle, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (cli.Stdout.IndexOf(needle, StringComparison.Ordinal) >= 0)
                    return true;
                System.Threading.Thread.Sleep(50);
            }
            return false;
        }
    }
}
