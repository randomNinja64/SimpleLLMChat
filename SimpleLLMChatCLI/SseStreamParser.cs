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
            Action<string> onReasoningChunk,
            Action<int> onReasoningSummary)
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
            DateTime reasoningStart = DateTime.MinValue;

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.StartsWith("data: ")) continue;

                string jsonPart = line.Substring(6);
                if (jsonPart == "[DONE]") break;

                if (jsonPart.Contains("\"error\""))
                {
                    Console.Write("[API Error] " + jsonPart.Trim() + "\n");
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
                            ref reasoningStart, partialToolCalls, onReasoningChunk, onReasoningSummary);
                    }
                }
                catch
                {
                    // ignore malformed JSON fragments
                }
            }

            CloseReasoningBlock(ref response, inReasoning, reasoningStart, onReasoningChunk, onReasoningSummary);

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
            ref DateTime reasoningStart,
            Dictionary<int, ToolRegistry.ToolCall> partialToolCalls,
            Action<string> onReasoningChunk,
            Action<int> onReasoningSummary)
        {
            string reasoningChunk = (string)choice["delta"]?["reasoning_content"]
                ?? (string)choice["delta"]?["reasoning"];
            if (!string.IsNullOrEmpty(reasoningChunk))
            {
                if (onReasoningChunk != null)
                {
                    if (!inReasoning) { Console.Write("[thinking]\n"); inReasoning = true; }
                    onReasoningChunk(reasoningChunk);
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
                        Console.Write("\n[/thinking]");
                        Console.WriteLine();
                        Console.WriteLine();
                        firstContent = true;
                    }
                    else
                    {
                        int secs = Math.Max(1, (int)(DateTime.UtcNow - reasoningStart).TotalSeconds);
                        response.ReasoningSeconds = secs;
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
                        Console.Write(content);
                        output.Append(content);
                    }
                }
                else
                {
                    Console.Write(content);
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
            bool inReasoning, DateTime reasoningStart,
            Action<string> onReasoningChunk, Action<int> onReasoningSummary)
        {
            if (!inReasoning) return;

            if (onReasoningChunk != null)
            {
                Console.Write("\n[/thinking]");
                Console.WriteLine();
                Console.WriteLine();
            }
            else
            {
                int secs = Math.Max(1, (int)(DateTime.UtcNow - reasoningStart).TotalSeconds);
                response.ReasoningSeconds = secs;
                onReasoningSummary?.Invoke(secs);
            }
        }
    }
}
