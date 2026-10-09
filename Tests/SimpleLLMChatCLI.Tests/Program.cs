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
            TestRunner.Run("TokenEstimator.basic", () =>
            {
                TestAssert.Equal(0, TokenEstimator.ApproximateTokens(0), "zero");
                TestAssert.Equal(3, TokenEstimator.ApproximateTokens(10), "10/3");
                TestAssert.True(TokenEstimator.ShouldSummarize(3000, 1000), "summarize");
                TestAssert.True(!TokenEstimator.ShouldSummarize(100, 1000), "no summarize");
            });

            TestRunner.Run("ToolApproval.roundtrip", () =>
            {
                string msg = ToolApproval.FormatApprovalMessage("read_file", "{\\\"filename\\\":\\\"a.txt\\\"}");
                string tool, arguments;
                TestAssert.True(ToolApproval.TryParseApprovalPrompt(
                    msg + "\n" + ToolApproval.ApprovalPrompt, out tool, out arguments), "parse");
                TestAssert.Equal("read_file", tool, "tool");
            });

            TestRunner.Run("ToolApproval.confidence_line_skipped", () =>
            {
                string args = "{\"command\":\"echo hi\"}";
                string msg = ToolApproval.FormatApprovalMessage("run_shell_command", args, "0.91");
                string tool, parsed;
                TestAssert.True(ToolApproval.TryParseApprovalPrompt(
                    msg + "\n" + ToolApproval.ApprovalPrompt, out tool, out parsed), "parse");
                TestAssert.Equal("run_shell_command", tool, "tool");
                TestAssert.Equal(args, parsed, "args");
                TestAssert.Contains(msg, "Jev confidence: 0.91", "confidence");
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

            TestRunner.Run("StatusPipe.ready_parse", () =>
            {
                TestAssert.True(StatusPipe.TryParseReadyLine(StatusPipe.ReadyLine), "ready");
                TestAssert.True(!StatusPipe.TryParseReadyLine("STATUS tokens=1"), "not ready");
            });

            TestRunner.Run("StatusPipe.approval_roundtrip", () =>
            {
                string args = "{\"command\":\"echo hi\\nnext\"}";
                string line = StatusPipe.FormatApproval("run_shell_command", args);
                string name, parsed, confidence;
                TestAssert.True(StatusPipe.TryParseApprovalLine(line, out name, out parsed, out confidence), "parse");
                TestAssert.Equal("run_shell_command", name, "name");
                TestAssert.Equal(args, parsed, "args");
                TestAssert.True(string.IsNullOrEmpty(confidence), "no confidence");

                string withConfidence = StatusPipe.FormatApproval("run_shell_command", args, 0.91);
                TestAssert.True(StatusPipe.TryParseApprovalLine(withConfidence, out name, out parsed, out confidence), "parse confidence");
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
                    WriteCliIni(ws.Path, server.BaseUrl + "/v1", "", "run_shell_command");
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
                    using (StatusPipeProbe probe = new StatusPipeProbe(cli.Id))
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
                    using (StatusPipeProbe probe = new StatusPipeProbe(cli.Id))
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
                    using (StatusPipeProbe probe = new StatusPipeProbe(cli.Id))
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
                        "auto",
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
                        "auto",
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
                        "auto",
                        server.BaseUrl + "/v1/systemone",
                        "jev-secret",
                        "jev-latest");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (StatusPipeProbe probe = new StatusPipeProbe(cli.Id))
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
                        "auto",
                        server.BaseUrl + "/v1/systemone",
                        "jev-secret",
                        "jev-latest",
                        false);

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (StatusPipeProbe probe = new StatusPipeProbe(cli.Id))
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
                        "auto",
                        server.BaseUrl + "/v1/systemone",
                        "jev-secret",
                        "");

                    using (CliProcess cli = new CliProcess(
                        Path.Combine(ws.Path, "SimpleLLMChatCLI.exe"),
                        "--no-banners",
                        ws.Path))
                    using (StatusPipeProbe probe = new StatusPipeProbe(cli.Id))
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
        }

        static TempWorkspace PackageCliWorkspace()
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

        static bool ShellToolsPackaged(TempWorkspace ws)
        {
            return File.Exists(Path.Combine(ws.Path, "tools", "ShellTools", "ShellTools.exe"));
        }

        static void WriteCliIni(string dir, string llmServer, string tools, string toolsRequiringApproval)
        {
            WriteCliIni(dir, llmServer, tools, toolsRequiringApproval, null, null, null, null, null);
        }

        static void WriteCliIni(
            string dir,
            string llmServer,
            string tools,
            string toolsRequiringApproval,
            string toolApprovalMode,
            string jevBaseUrl,
            string jevApiKey,
            string jevModel,
            bool? showJevConfidence = null)
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
                "contextWindowSize=0\r\n" +
                "[Tools]\r\n" +
                "tools=" + (tools ?? "") + "\r\n" +
                "toolsrequiringapproval=" + (toolsRequiringApproval ?? "") + "\r\n" +
                "toolapprovalmode=" + (toolApprovalMode ?? "manual") + "\r\n" +
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

        static bool WaitStdoutContains(CliProcess cli, string needle, int timeoutMs)
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
