using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SimpleLLMChatCLI
{
    internal static class SseStreamParser
    {
        public static LLMClient.LLMCompletionResponse Parse(
            TextReader reader,
            Action<string> onContentChunk,
            Action<string> onReasoningChunk,
            Action<int> onReasoningSummary,
            Action onContentStart = null,
            Action startBlock = null)
        {
            LLMClient.LLMCompletionResponse response = new LLMClient.LLMCompletionResponse
            {
                Content = string.Empty,
                ToolCalls = new List<ToolRegistry.ToolCall>(),
                FinishReason = string.Empty
            };

            StringBuilder output = new StringBuilder();
            Dictionary<int, ToolRegistry.ToolCall> partialToolCalls = new Dictionary<int, ToolRegistry.ToolCall>();
            bool inReasoning = false;
            bool firstContent = true;
            bool reasoningEndedWithNewline = false;
            DateTime reasoningStart = DateTime.MinValue;

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.StartsWith("data: ")) continue;

                string jsonPart = line.Substring(6);
                if (jsonPart == "[DONE]") break;

                if (jsonPart.Contains("\"error\""))
                {
                    if (onContentChunk != null)
                    {
                        startBlock?.Invoke();
                        onContentChunk("[API Error] " + jsonPart.Trim() + "\n");
                    }
                    continue;
                }

                try
                {
                    JObject obj = JObject.Parse(jsonPart);
                    JArray choices = (JArray)obj["choices"];
                    if (choices == null) continue;

                    foreach (JObject choice in choices)
                    {
                        ProcessChoice(choice, ref response, output, ref inReasoning, ref firstContent,
                            ref reasoningEndedWithNewline, ref reasoningStart, partialToolCalls,
                            onContentChunk, onReasoningChunk, onReasoningSummary, onContentStart, startBlock);
                    }
                }
                catch
                {
                    // ignore malformed JSON fragments
                }
            }

            CloseReasoningBlock(ref response, inReasoning, reasoningEndedWithNewline, reasoningStart,
                onReasoningChunk, onReasoningSummary);

            response.ToolCalls.AddRange(partialToolCalls.Values);
            response.Content = output.ToString();
            return response;
        }

        private static void ProcessChoice(
            JObject choice,
            ref LLMClient.LLMCompletionResponse response,
            StringBuilder output,
            ref bool inReasoning,
            ref bool firstContent,
            ref bool reasoningEndedWithNewline,
            ref DateTime reasoningStart,
            Dictionary<int, ToolRegistry.ToolCall> partialToolCalls,
            Action<string> onContentChunk,
            Action<string> onReasoningChunk,
            Action<int> onReasoningSummary,
            Action onContentStart,
            Action startBlock)
        {
            string reasoningChunk = (string)choice["delta"]?["reasoning_content"]
                ?? (string)choice["delta"]?["reasoning"];
            if (!string.IsNullOrEmpty(reasoningChunk))
            {
                if (onReasoningChunk != null)
                {
                    if (!inReasoning)
                    {
                        startBlock?.Invoke();
                        onReasoningChunk("[thinking]\n");
                        inReasoning = true;
                    }
                    onReasoningChunk(reasoningChunk);
                    reasoningEndedWithNewline = reasoningChunk.EndsWith("\n");
                }
                else if (!inReasoning)
                {
                    reasoningStart = DateTime.UtcNow;
                    inReasoning = true;
                }
            }

            string content = (string)choice["delta"]?["content"];
            if (!string.IsNullOrEmpty(content))
            {
                if (inReasoning)
                {
                    if (onReasoningChunk != null)
                    {
                        EmitReasoningClose(onReasoningChunk, reasoningEndedWithNewline);
                        firstContent = true;
                    }
                    else
                    {
                        int secs = Math.Max(1, (int)(DateTime.UtcNow - reasoningStart).TotalSeconds);
                        onReasoningSummary?.Invoke(secs);
                    }
                    inReasoning = false;
                }
                if (firstContent)
                {
                    content = content.TrimStart('\n');
                    if (content.Length > 0)
                    {
                        firstContent = false;
                        // Lets the caller pad the block and decide whether to
                        // print the assistant name prefix before the first chunk.
                        onContentStart?.Invoke();
                        onContentChunk?.Invoke(content);
                        output.Append(content);
                    }
                }
                else
                {
                    onContentChunk?.Invoke(content);
                    output.Append(content);
                }
            }

            string finishReason = (string)choice["finish_reason"];
            if (!string.IsNullOrEmpty(finishReason))
                response.FinishReason = finishReason;

            JArray toolCalls = (JArray)choice["delta"]?["tool_calls"];
            if (toolCalls != null)
                AccumulateToolCalls(toolCalls, partialToolCalls);
        }

        private static void AccumulateToolCalls(JArray toolCalls, Dictionary<int, ToolRegistry.ToolCall> partialToolCalls)
        {
            foreach (JObject call in toolCalls)
            {
                int index = call["index"]?.Value<int>() ?? 0;
                if (!partialToolCalls.ContainsKey(index))
                    partialToolCalls[index] = new ToolRegistry.ToolCall();

                ToolRegistry.ToolCall tc = partialToolCalls[index];
                string id = (string)call["id"];
                if (!string.IsNullOrEmpty(id)) tc.Id = id;

                JObject function = (JObject)call["function"];
                if (function != null)
                {
                    string name = (string)function["name"];
                    string argsChunk = (string)function["arguments"];
                    if (!string.IsNullOrEmpty(name)) tc.Name = name;
                    if (!string.IsNullOrEmpty(argsChunk)) tc.Arguments += argsChunk;
                }
            }
        }

        private static void CloseReasoningBlock(
            ref LLMClient.LLMCompletionResponse response,
            bool inReasoning, bool reasoningEndedWithNewline, DateTime reasoningStart,
            Action<string> onReasoningChunk, Action<int> onReasoningSummary)
        {
            if (!inReasoning) return;

            if (onReasoningChunk != null)
            {
                EmitReasoningClose(onReasoningChunk, reasoningEndedWithNewline);
            }
            else
            {
                int secs = Math.Max(1, (int)(DateTime.UtcNow - reasoningStart).TotalSeconds);
                onReasoningSummary?.Invoke(secs);
            }
        }

        /// <summary>
        /// Closes an open [thinking] block on its own line. The blank line that
        /// separates it from the next block is added by the caller's StartBlock.
        /// </summary>
        private static void EmitReasoningClose(Action<string> onReasoningChunk, bool reasoningEndedWithNewline)
        {
            onReasoningChunk(reasoningEndedWithNewline ? "[/thinking]\n" : "\n[/thinking]\n");
        }
    }
}
