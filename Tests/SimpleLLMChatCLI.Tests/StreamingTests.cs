using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace SimpleLLMChatCLI.Tests
{
    /// <summary>Records what the parser tells a sink, in order.</summary>
    internal sealed class RecordingSink : StreamSink
    {
        public readonly List<string> Events = new List<string>();
        public bool ShowReasoning;

        public override bool ShowsReasoning { get { return ShowReasoning; } }
        public override void Error(string text) { Events.Add("error:" + text); }
        public override void ContentStart() { Events.Add("content-start"); }
        public override void Content(string text) { Events.Add("content:" + text); }
        public override void ReasoningStarted() { Events.Add("reasoning-start"); }
        public override void Reasoning(string text) { Events.Add("reasoning:" + text); }
        public override void ReasoningEnded() { Events.Add("reasoning-end"); }
        public override void ToolCallStarted(string name) { Events.Add("tool:" + name); }
        public override void ToolCallArgs(string chunk) { Events.Add("args:" + chunk); }

        public string Joined() { return string.Join("|", Events.ToArray()); }
    }

    /// <summary>Yields its lines, then fails like a dropped connection.</summary>
    internal sealed class ThrowingReader : TextReader
    {
        private readonly Queue<string> _lines;

        public ThrowingReader(params string[] lines)
        {
            _lines = new Queue<string>(lines);
        }

        public override string ReadLine()
        {
            if (_lines.Count > 0)
                return _lines.Dequeue();
            throw new IOException("connection dropped");
        }
    }

    /// <summary>
    /// SseStreamParser, the sinks and ChatOutput, driven in-process.
    /// </summary>
    internal static class StreamingTests
    {
        private const string Done = "data: [DONE]\n\n";

        public static void Run()
        {
            ParserTests();
            SinkTests();
        }

        // ---- SSE event builders ----

        private static string Line(JObject obj)
        {
            return "data: " + obj.ToString(Formatting.None) + "\n\n";
        }

        private static string Choice(JObject delta, string finishReason = null)
        {
            JObject choice = new JObject();
            choice["delta"] = delta;
            if (finishReason != null)
                choice["finish_reason"] = finishReason;
            JObject root = new JObject();
            root["choices"] = new JArray(choice);
            return Line(root);
        }

        private static string Text(string content)
        {
            JObject delta = new JObject();
            delta["content"] = content;
            return Choice(delta);
        }

        private static string Reasoning(string text, string field = "reasoning_content")
        {
            JObject delta = new JObject();
            delta[field] = text;
            return Choice(delta);
        }

        private static string Finish(string reason)
        {
            return Choice(new JObject(), reason);
        }

        private static string Tool(int? index, string id, string name, string arguments)
        {
            JObject call = new JObject();
            if (index.HasValue)
                call["index"] = index.Value;
            if (id != null)
                call["id"] = id;
            if (name != null || arguments != null)
            {
                JObject function = new JObject();
                if (name != null)
                    function["name"] = name;
                if (arguments != null)
                    function["arguments"] = arguments;
                call["function"] = function;
            }
            JObject delta = new JObject();
            delta["tool_calls"] = new JArray(call);
            return Choice(delta);
        }

        private static LLMClient.LLMCompletionResponse Parse(StreamSink sink, params string[] events)
        {
            return new SseStreamParser(sink).Read(new StringReader(string.Concat(events)));
        }

        // ---- Parser ----

        private static void ParserTests()
        {
            TestRunner.Run("sse.content_chunks", () =>
            {
                RecordingSink sink = new RecordingSink();
                LLMClient.LLMCompletionResponse r = Parse(sink,
                    Text("\n\nHel"), Text("lo"), Finish("stop"), Done, Text("after-done"));
                TestAssert.Equal("content-start|content:Hel|content:lo", sink.Joined(), "events");
                TestAssert.Equal("Hello", r.Content, "content");
                TestAssert.Equal("stop", r.FinishReason, "finish reason");
                TestAssert.Equal(0, r.ToolCalls.Count, "no tool calls");

                // A first chunk that is only line breaks is dropped, and the next one still counts as first.
                sink = new RecordingSink();
                r = Parse(sink, Text("\n"), Text("\nA\n"), Text("\nB"), Done);
                TestAssert.Equal("content-start|content:A\n|content:\nB", sink.Joined(), "leading breaks trimmed once");
                TestAssert.Equal("A\n\nB", r.Content, "inner breaks kept");
            });

            TestRunner.Run("sse.line_framing", () =>
            {
                RecordingSink sink = new RecordingSink();
                LLMClient.LLMCompletionResponse r = new SseStreamParser(sink).Read(new StringReader(
                    ": keep-alive\r\n" +
                    "event: message\r\n" +
                    "data:{\"choices\":[{\"delta\":{\"content\":\"a\"}}]}\r\n" +
                    "\r\n" +
                    "data: this is not json\n" +
                    "data: {\"object\":\"chat.completion.chunk\"}\n" +
                    "data: {\"choices\":[{\"delta\":null}]}\n" +
                    "data: {\"choices\":[{\"delta\":{\"content\":\"b\"}}]}\n"));
                TestAssert.Equal("content-start|content:a|content:b", sink.Joined(), "events");
                TestAssert.Equal("ab", r.Content, "content without [DONE]");
                TestAssert.Equal("", r.FinishReason, "no finish reason");
            });

            TestRunner.Run("sse.error_field", () =>
            {
                RecordingSink sink = new RecordingSink();
                LLMClient.LLMCompletionResponse r = Parse(sink,
                    "data: {\"error\":{\"message\":\"quota-exceeded\"}}\n\n",
                    Done);
                TestAssert.Equal(1, sink.Events.Count, "one event");
                TestAssert.True(sink.Events[0].StartsWith("error:[API Error] ", StringComparison.Ordinal), "error prefix");
                TestAssert.Contains(sink.Events[0], "quota-exceeded", "error body");
                TestAssert.Equal("", r.Content, "no content");

                sink = new RecordingSink();
                r = Parse(sink,
                    "data: {\"error\":null,\"choices\":[{\"delta\":{\"content\":\"error\"}}]}\n\n",
                    Done);
                TestAssert.Equal("content-start|content:error", sink.Joined(), "null error and the word error are content");
            });

            TestRunner.Run("sse.reasoning", () =>
            {
                // Both field names; reasoning is not part of the reply text.
                RecordingSink sink = new RecordingSink();
                LLMClient.LLMCompletionResponse r = Parse(sink,
                    Reasoning("one"), Reasoning("two", "reasoning"), Text("answer"), Finish("stop"), Done);
                TestAssert.Equal(
                    "reasoning-start|reasoning:one|reasoning:two|reasoning-end|content-start|content:answer",
                    sink.Joined(), "events");
                TestAssert.Equal("answer", r.Content, "content");

                // Content after a shown thinking block starts a block of its own.
                sink = new RecordingSink { ShowReasoning = true };
                Parse(sink, Text("a"), Reasoning("r"), Text("\nb"), Done);
                TestAssert.Equal(
                    "content-start|content:a|reasoning-start|reasoning:r|reasoning-end|content-start|content:b",
                    sink.Joined(), "shown reasoning restarts content");

                sink = new RecordingSink { ShowReasoning = false };
                Parse(sink, Text("a"), Reasoning("r"), Text("b"), Done);
                TestAssert.Equal(
                    "content-start|content:a|reasoning-start|reasoning:r|reasoning-end|content:b",
                    sink.Joined(), "hidden reasoning continues content");

                // Closed when the stream ends or a tool call follows.
                sink = new RecordingSink();
                Parse(sink, Reasoning("r"));
                TestAssert.Equal("reasoning-start|reasoning:r|reasoning-end", sink.Joined(), "closed at end of stream");

                sink = new RecordingSink();
                Parse(sink, Reasoning("r"), Tool(0, "call_1", "lookup", ""), Done);
                TestAssert.Equal("reasoning-start|reasoning:r|reasoning-end|tool:lookup", sink.Joined(), "closed before tool call");
            });

            TestRunner.Run("sse.tool_call_streaming", () =>
            {
                RecordingSink sink = new RecordingSink();
                LLMClient.LLMCompletionResponse r = Parse(sink,
                    Text("Let me check."),
                    Tool(0, "call_1", "read_file", ""),
                    Tool(0, null, null, "{\"a\":"),
                    Tool(0, null, null, "1}"),
                    Finish("tool_calls"),
                    Done);
                TestAssert.Equal(
                    "content-start|content:Let me check.|tool:read_file|args:{\"a\":|args:1}",
                    sink.Joined(), "events");
                TestAssert.Equal("Let me check.", r.Content, "text beside the call kept");
                TestAssert.Equal("tool_calls", r.FinishReason, "finish reason");
                TestAssert.Equal(1, r.ToolCalls.Count, "one call");
                TestAssert.Equal("call_1", r.ToolCalls[0].Id, "id");
                TestAssert.Equal("read_file", r.ToolCalls[0].Name, "name");
                TestAssert.Equal("{\"a\":1}", r.ToolCalls[0].Arguments, "arguments joined");
            });

            TestRunner.Run("sse.tool_call_parallel", () =>
            {
                RecordingSink sink = new RecordingSink();
                LLMClient.LLMCompletionResponse r = Parse(sink,
                    Tool(0, "call_a", "first", ""),
                    Tool(1, "call_b", "second", ""),
                    Tool(0, null, null, "{\"x\":1}"),
                    Tool(1, null, null, "{\"y\":2}"),
                    Done);
                TestAssert.Equal(2, r.ToolCalls.Count, "two calls");
                TestAssert.Equal("call_a", r.ToolCalls[0].Id, "first id");
                TestAssert.Equal("first", r.ToolCalls[0].Name, "first name");
                TestAssert.Equal("{\"x\":1}", r.ToolCalls[0].Arguments, "first arguments");
                TestAssert.Equal("call_b", r.ToolCalls[1].Id, "second id");
                TestAssert.Equal("second", r.ToolCalls[1].Name, "second name");
                TestAssert.Equal("{\"y\":2}", r.ToolCalls[1].Arguments, "second arguments");
            });

            TestRunner.Run("sse.tool_call_edge_cases", () =>
            {
                // Arguments that arrive before the name are held, then streamed whole.
                RecordingSink sink = new RecordingSink();
                LLMClient.LLMCompletionResponse r = Parse(sink,
                    Tool(0, "call_late", null, "{\"q\":"),
                    Tool(0, null, "late", "2}"),
                    Done);
                TestAssert.Equal("tool:late|args:{\"q\":2}", sink.Joined(), "late name");
                TestAssert.Equal("{\"q\":2}", r.ToolCalls[0].Arguments, "late name arguments");

                // A server that repeats the name on every chunk announces the call once.
                sink = new RecordingSink();
                r = Parse(sink,
                    Tool(0, "call_rep", "repeat", "{\"k\":"),
                    Tool(0, "call_rep", "repeat", "3}"),
                    Done);
                TestAssert.Equal("tool:repeat|args:{\"k\":|args:3}", sink.Joined(), "repeated name");
                TestAssert.Equal(1, r.ToolCalls.Count, "repeated name is one call");

                // No index means index 0; an entry with no function is not a crash.
                sink = new RecordingSink();
                r = Parse(sink,
                    Tool(null, "call_noidx", null, null),
                    Tool(null, null, "noindex", "{}"),
                    Done);
                TestAssert.Equal("tool:noindex|args:{}", sink.Joined(), "missing index");
                TestAssert.Equal(1, r.ToolCalls.Count, "missing index is one call");
                TestAssert.Equal("call_noidx", r.ToolCalls[0].Id, "id from the function-less entry");
            });

            TestRunner.Run("sse.dropped_connection", () =>
            {
                string first = Text("partial").TrimEnd('\n');

                // Not a stop: the failure reaches the caller.
                bool threw = false;
                try
                {
                    new SseStreamParser(new RecordingSink()).Read(new ThrowingReader(first));
                }
                catch (IOException)
                {
                    threw = true;
                }
                TestAssert.True(threw, "read failure propagates");

                // A stop aborts the connection; what streamed so far is kept.
                Cancel.Requested = true;
                try
                {
                    RecordingSink sink = new RecordingSink();
                    LLMClient.LLMCompletionResponse r =
                        new SseStreamParser(sink).Read(new ThrowingReader(first));
                    TestAssert.Equal("partial", r.Content, "partial content kept");
                    TestAssert.Equal("content-start|content:partial", sink.Joined(), "events");
                }
                finally
                {
                    Cancel.Requested = false;
                }
            });
        }

        // ---- Sinks and ChatOutput ----

        /// <summary>Runs the action with console output captured, from a block-separated start.</summary>
        internal static string Capture(Action action)
        {
            TextWriter original = Console.Out;
            StringWriter captured = new StringWriter();
            Console.SetOut(captured);
            ChatOutput.MarkSeparated();
            try
            {
                action();
            }
            finally
            {
                Console.SetOut(original);
                ChatOutput.MarkSeparated();
            }
            return captured.ToString();
        }

        private static TurnSettings Settings()
        {
            return new TurnSettings
            {
                AssistantName = "LLM",
                EnabledTools = new List<string>(),
                EnabledSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                ApprovalSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                ThinkingDisplay = ChatBlockDisplayMode.Shown,
                ToolCallDisplay = ChatBlockDisplayMode.Shown,
                ToolOutputDisplay = ChatBlockDisplayMode.Shown
            };
        }

        private static void SinkTests()
        {
            TestRunner.Run("chatoutput.blank_line_rules", () =>
            {
                TestAssert.Equal("a\n\nb", Capture(() => ChatOutput.Write("a\n\n\n\nb")), "blank run capped at one");
                TestAssert.Equal("a\n\nb", Capture(() => ChatOutput.Write("a\n   \n\t\nb")), "whitespace-only lines are blank");
                TestAssert.Equal("x", Capture(() => ChatOutput.Write("\n\nx")), "no blank lines at a separated start");
                TestAssert.Equal("  indented\nnext", Capture(() => ChatOutput.Write("  indented\r\nnext")), "indent kept, CRLF folded");
                TestAssert.Equal("a\n\nb", Capture(() =>
                {
                    ChatOutput.Write("a");
                    ChatOutput.StartBlock();
                    ChatOutput.StartBlock();
                    ChatOutput.Write("b");
                }), "StartBlock pads to one blank line");
                TestAssert.Equal("a\nb\n", Capture(() =>
                {
                    ChatOutput.Write("a");
                    ChatOutput.EndLine();
                    ChatOutput.EndLine();
                    ChatOutput.Write("b\n");
                    ChatOutput.EndLine();
                }), "EndLine only ends an open line");
            });

            TestRunner.Run("sink.console_plain_reply", () =>
            {
                string output = Capture(() =>
                {
                    StreamSink sink = StreamSink.For(Settings());
                    Parse(sink, Text("hello "), Text("there"), Finish("stop"), Done);
                    sink.EndResponse();
                    sink.EndLine();
                });
                TestAssert.Equal("LLM: hello there\n", output, "name and reply on one line");
            });

            TestRunner.Run("sink.console_thinking", () =>
            {
                string shown = Capture(() =>
                {
                    StreamSink sink = StreamSink.For(Settings());
                    Parse(sink, Reasoning("mulling"), Text("answer"), Finish("stop"), Done);
                    sink.EndResponse();
                    sink.EndLine();
                });
                TestAssert.Equal("LLM:\n\n[thinking]\nmulling\n[/thinking]\n\nanswer\n", shown, "shown");

                TurnSettings hiddenSettings = Settings();
                hiddenSettings.ThinkingDisplay = ChatBlockDisplayMode.Hidden;
                string hidden = Capture(() =>
                {
                    StreamSink sink = StreamSink.For(hiddenSettings);
                    Parse(sink, Reasoning("mulling"), Text("answer"), Finish("stop"), Done);
                    sink.EndResponse();
                    sink.EndLine();
                });
                TestAssert.Equal("LLM:\n\n[thought for 1 second]\n\nanswer\n", hidden, "hidden");
            });

            TestRunner.Run("sink.console_tool_blocks", () =>
            {
                string output = Capture(() =>
                {
                    StreamSink sink = StreamSink.For(Settings());
                    Parse(sink,
                        Tool(0, "call_1", "read_file", ""),
                        Tool(0, null, null, "{\"a\":1}"),
                        Finish("tool_calls"),
                        Done);
                    sink.EndResponse();
                    sink.ToolOutput("out", 0);
                    Parse(sink, Text("final"), Finish("stop"), Done);
                    sink.EndResponse();
                    sink.EndLine();
                });
                TestAssert.Equal(
                    "LLM:\n\n[tool call] read_file\n{\"a\":1}\n[/tool call]\n\n" +
                    "[tool output]\nout\n[/tool output]\n\nfinal\n",
                    output, "tool call, output, then reply");
            });

            TestRunner.Run("sink.console_tool_call_awaiting_approval", () =>
            {
                // The approval prompt shows the call, so the block is not also streamed.
                TurnSettings manual = Settings();
                manual.ApprovalSet.Add("read_file");
                string suppressed = Capture(() =>
                {
                    StreamSink sink = StreamSink.For(manual);
                    Parse(sink, Tool(0, "call_1", "READ_FILE", "{\"a\":1}"), Done);
                    sink.EndResponse();
                });
                TestAssert.Equal("", suppressed, "suppressed under manual approval");

                TurnSettings auto = Settings();
                auto.ApprovalSet.Add("read_file");
                auto.AutoApproval = true;
                string streamed = Capture(() =>
                {
                    StreamSink sink = StreamSink.For(auto);
                    Parse(sink, Tool(0, "call_1", "read_file", "{\"a\":1}"), Done);
                    sink.EndResponse();
                });
                TestAssert.Equal("LLM:\n\n[tool call] read_file\n{\"a\":1}\n[/tool call]\n", streamed, "streamed under auto approval");
            });

            TestRunner.Run("sink.console_hidden_display_modes", () =>
            {
                TurnSettings settings = Settings();
                settings.ToolCallDisplay = ChatBlockDisplayMode.Hidden;
                settings.ToolOutputDisplay = ChatBlockDisplayMode.Hidden;

                string call = Capture(() =>
                {
                    StreamSink sink = StreamSink.For(settings);
                    Parse(sink, Tool(0, "call_1", "read_file", "{\"a\":1}"), Done);
                    sink.EndResponse();
                });
                TestAssert.Equal("LLM:\n\n[tool call] read_file\n[/tool call]\n", call, "arguments hidden");

                string toolOutput = Capture(() => StreamSink.For(settings).ToolOutput("lots of text", 3));
                TestAssert.Equal("LLM:\n\n[tool output]\nExit Code: 3\n[/tool output]\n", toolOutput, "output reduced to exit code");
            });

            TestRunner.Run("sink.output_only", () =>
            {
                TurnSettings settings = Settings();
                settings.OutputOnly = true;

                string output = Capture(() =>
                {
                    StreamSink sink = StreamSink.For(settings);
                    sink.Notice("[a notice]");
                    Parse(sink,
                        Reasoning("mulling"),
                        Tool(0, "call_1", "read_file", "{\"a\":1}"),
                        Finish("tool_calls"),
                        Done);
                    sink.EndResponse();
                    sink.BeginApproval();
                    sink.ToolOutput("tool text", 0);
                    Parse(sink, Text("answer"), Finish("stop"), Done);
                    sink.EndResponse();
                    sink.EndLine();
                });
                TestAssert.Equal("answer", output, "reply text and nothing else");

                string error = Capture(() => StreamSink.For(settings).Error("it broke"));
                TestAssert.Equal("it broke\n", error, "errors are still shown");
            });

            TestRunner.Run("sink.inline_think_tags_on_console", () =>
            {
                TurnSettings settings = Settings();
                settings.OutputOnly = true;
                string output = Capture(() =>
                {
                    StreamSink sink = StreamSink.For(settings);
                    sink.Content("pre<THINK>hidden</think>post<think>open");
                    sink.EndResponse();
                });
                TestAssert.Equal("pre<THINK>hidden</think>post<think>open", output, "terminal shows tags as sent");
            });
        }
    }
}
