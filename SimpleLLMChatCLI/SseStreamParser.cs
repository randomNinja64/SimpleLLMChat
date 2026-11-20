using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SimpleLLMChatCLI
{
    /// <summary>
    /// Reads one streamed chat completion, sending output to a sink as it arrives.
    /// </summary>
    internal sealed class SseStreamParser
    {
        private readonly StreamSink _sink;
        private readonly StringBuilder _output = new StringBuilder();
        private readonly Dictionary<int, ToolRegistry.ToolCall> _toolCalls = new Dictionary<int, ToolRegistry.ToolCall>();
        private readonly Dictionary<int, int> _streamedArgumentLength = new Dictionary<int, int>();
        private string _finishReason = string.Empty;
        private bool _inReasoning;
        private bool _firstContent = true;

        public SseStreamParser(StreamSink sink)
        {
            _sink = sink;
        }

        public LLMClient.LLMCompletionResponse Read(TextReader reader)
        {
            string line;
            while (true)
            {
                // A stop aborts the connection mid-read; keep what streamed so far.
                try { line = reader.ReadLine(); }
                catch { if (!Cancel.Requested) throw; break; }
                if (line == null) break;

                if (!line.StartsWith("data:")) continue;

                string jsonPart = line.Substring(5).TrimStart(' ');
                if (jsonPart == "[DONE]") break;

                JObject obj;
                try
                {
                    obj = JObject.Parse(jsonPart);
                }
                catch
                {
                    continue;
                }

                // A content delta whose text is "error" also contains that substring.
                // Only a real error property is an API failure.
                JToken errorToken = obj["error"];
                if (errorToken != null && errorToken.Type != JTokenType.Null)
                {
                    _sink.Error("[API Error] " + jsonPart.Trim());
                    continue;
                }

                try
                {
                    JArray choices = (JArray)obj["choices"];
                    if (choices == null) continue;

                    foreach (JObject choice in choices)
                        ProcessChoice(choice);
                }
                catch
                {
                    // ignore malformed JSON fragments
                }
            }

            CloseReasoningIfOpen();

            return new LLMClient.LLMCompletionResponse(
                _output.ToString(), new List<ToolRegistry.ToolCall>(_toolCalls.Values), _finishReason);
        }

        private void ProcessChoice(JObject choice)
        {
            string reasoningChunk = (string)choice["delta"]?["reasoning_content"]
                ?? (string)choice["delta"]?["reasoning"];
            if (!string.IsNullOrEmpty(reasoningChunk))
            {
                if (!_inReasoning)
                {
                    _inReasoning = true;
                    _sink.ReasoningStarted();
                }
                _sink.Reasoning(reasoningChunk);
            }

            string content = (string)choice["delta"]?["content"];
            if (!string.IsNullOrEmpty(content))
            {
                // Content after a shown thinking block starts a new block of its own.
                if (_inReasoning && _sink.ShowsReasoning)
                    _firstContent = true;
                CloseReasoningIfOpen();

                if (_firstContent)
                    content = content.TrimStart('\n');
                if (content.Length > 0)
                {
                    if (_firstContent)
                    {
                        _firstContent = false;
                        _sink.ContentStart();
                    }
                    _sink.Content(content);
                    _output.Append(content);
                }
            }

            string finishReason = (string)choice["finish_reason"];
            if (!string.IsNullOrEmpty(finishReason))
                _finishReason = finishReason;

            JArray toolCalls = (JArray)choice["delta"]?["tool_calls"];
            if (toolCalls != null)
            {
                // Close any open thinking block before tool-call UI streams — a model that
                // reasons and then calls a tool with no text in between would otherwise leave
                // [thinking] open, and the [tool call] block would render as if nested inside it.
                CloseReasoningIfOpen();

                AccumulateToolCalls(toolCalls);
            }
        }

        private void AccumulateToolCalls(JArray toolCalls)
        {
            foreach (JObject call in toolCalls)
            {
                int index = call["index"]?.Value<int>() ?? 0;
                if (!_toolCalls.ContainsKey(index))
                {
                    _toolCalls[index] = new ToolRegistry.ToolCall();
                    _streamedArgumentLength[index] = 0;
                }

                ToolRegistry.ToolCall tc = _toolCalls[index];
                string id = (string)call["id"];
                if (!string.IsNullOrEmpty(id)) tc.Id = id;

                JObject function = (JObject)call["function"];
                if (function == null)
                    continue;

                string name = (string)function["name"];
                string argsChunk = (string)function["arguments"];

                if (!string.IsNullOrEmpty(name))
                {
                    tc.Name = name;
                    // Announce the tool call as soon as its name is known, before any arguments arrive.
                    if (_streamedArgumentLength[index] == 0)
                        _sink.ToolCallStarted(name);
                }

                if (!string.IsNullOrEmpty(argsChunk))
                {
                    tc.Arguments += argsChunk;
                    // Arguments that arrived before the name are streamed once it is known.
                    if (!string.IsNullOrEmpty(tc.Name))
                    {
                        _sink.ToolCallArgs(tc.Arguments.Substring(_streamedArgumentLength[index]));
                        _streamedArgumentLength[index] = tc.Arguments.Length;
                    }
                }
            }
        }

        private void CloseReasoningIfOpen()
        {
            if (!_inReasoning)
                return;

            _inReasoning = false;
            _sink.ReasoningEnded();
        }
    }
}
