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
            Action<ToolRegistry.ToolCall> onToolCallChunk = null,
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
            Dictionary<int, int> toolCallArgumentLength = new Dictionary<int, int>();
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
                            ref reasoningEndedWithNewline, ref reasoningStart, partialToolCalls, toolCallArgumentLength,
                            onContentChunk, onReasoningChunk, onReasoningSummary, onToolCallChunk, onContentStart, startBlock);
                    }
                }
                catch
                {
                    // ignore malformed JSON fragments
                }
            }

            CloseReasoningIfOpen(ref inReasoning, reasoningEndedWithNewline, reasoningStart,
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
            Dictionary<int, int> toolCallArgumentLength,
            Action<string> onContentChunk,
            Action<string> onReasoningChunk,
            Action<int> onReasoningSummary,
            Action<ToolRegistry.ToolCall> onToolCallChunk,
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
                    bool wasShowingReasoning = onReasoningChunk != null;
                    CloseReasoningIfOpen(ref inReasoning, reasoningEndedWithNewline, reasoningStart,
                        onReasoningChunk, onReasoningSummary);
                    if (wasShowingReasoning)
                        firstContent = true;
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
            {
                // Close any open thinking block before tool-call UI streams — a model that
                // reasons and then calls a tool with no text in between would otherwise leave
                // [thinking] open, and the [tool call] block would render as if nested inside it.
                CloseReasoningIfOpen(ref inReasoning, reasoningEndedWithNewline, reasoningStart,
                    onReasoningChunk, onReasoningSummary);

                AccumulateToolCalls(toolCalls, partialToolCalls, toolCallArgumentLength, onToolCallChunk);
            }
        }

        private static void AccumulateToolCalls(
            JArray toolCalls,
            Dictionary<int, ToolRegistry.ToolCall> partialToolCalls,
            Dictionary<int, int> toolCallArgumentLength,
            Action<ToolRegistry.ToolCall> onToolCallChunk)
        {
            foreach (JObject call in toolCalls)
            {
                int index = call["index"]?.Value<int>() ?? 0;
                if (!partialToolCalls.ContainsKey(index))
                {
                    partialToolCalls[index] = new ToolRegistry.ToolCall();
                    toolCallArgumentLength[index] = 0;
                }

                ToolRegistry.ToolCall tc = partialToolCalls[index];
                string id = (string)call["id"];
                if (!string.IsNullOrEmpty(id)) tc.Id = id;

                JObject function = (JObject)call["function"];
                if (function != null)
                {
                    string name = (string)function["name"];
                    string argsChunk = (string)function["arguments"];

                    if (!string.IsNullOrEmpty(name))
                    {
                        tc.Name = name;
                        // Announce the tool call as soon as its name is known, before any arguments arrive.
                        if (onToolCallChunk != null && toolCallArgumentLength[index] == 0)
                            onToolCallChunk(new ToolRegistry.ToolCall { Name = name, Arguments = "", Id = tc.Id });
                    }

                    if (!string.IsNullOrEmpty(argsChunk))
                    {
                        tc.Arguments += argsChunk;
                        if (onToolCallChunk != null && !string.IsNullOrEmpty(tc.Name))
                        {
                            int alreadyStreamed = toolCallArgumentLength[index];
                            if (tc.Arguments.Length > alreadyStreamed)
                            {
                                string newChunk = tc.Arguments.Substring(alreadyStreamed);
                                onToolCallChunk(new ToolRegistry.ToolCall { Name = tc.Name, Arguments = newChunk, Id = tc.Id });
                                toolCallArgumentLength[index] = tc.Arguments.Length;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Closes an in-progress reasoning span, if any: emits the [/thinking] closing tag
        /// when reasoning text is being streamed, or reports the elapsed time via
        /// <paramref name="onReasoningSummary"/> when reasoning is hidden.
        /// </summary>
        private static void CloseReasoningIfOpen(
            ref bool inReasoning, bool reasoningEndedWithNewline, DateTime reasoningStart,
            Action<string> onReasoningChunk, Action<int> onReasoningSummary)
        {
            if (!inReasoning) return;

            if (onReasoningChunk != null)
            {
                // Closing tag on its own line; blank line before the next block
                // comes from the caller's StartBlock.
                onReasoningChunk(reasoningEndedWithNewline ? "[/thinking]\n" : "\n[/thinking]\n");
            }
            else
            {
                int secs = Math.Max(1, (int)(DateTime.UtcNow - reasoningStart).TotalSeconds);
                onReasoningSummary?.Invoke(secs);
            }

            inReasoning = false;
        }
    }
}
