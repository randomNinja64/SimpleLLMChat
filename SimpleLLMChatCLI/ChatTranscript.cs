using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SimpleLLMChatCLI
{
    /// <summary>
    /// The saved-chat file format. It records the conversation exactly as it is
    /// sent to the model (roles, tool calls, tool results, images), so a later
    /// loader can rebuild the ChatMessage list and continue the session.
    /// </summary>
    internal static class ChatTranscript
    {
        public static void Save(string path, List<LLMClient.ChatMessage> conversation, string assistantName, string reasoningEffort)
        {
            JObject root = new JObject
            {
                ["saved_at"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["assistant_name"] = assistantName ?? string.Empty
            };
            if (!string.IsNullOrEmpty(reasoningEffort))
                root["reasoning_effort"] = reasoningEffort;

            JArray messages = new JArray();
            foreach (LLMClient.ChatMessage message in conversation)
                messages.Add(ToJson(message));
            root["messages"] = messages;

            File.WriteAllText(path, root.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
        }

        private static JObject ToJson(LLMClient.ChatMessage message)
        {
            JObject obj = new JObject
            {
                ["role"] = message.Role ?? string.Empty,
                ["content"] = message.Content ?? string.Empty
            };

            if (!string.IsNullOrEmpty(message.ToolCallId))
                obj["tool_call_id"] = message.ToolCallId;

            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                JArray calls = new JArray();
                foreach (ToolRegistry.ToolCall call in message.ToolCalls)
                {
                    calls.Add(new JObject
                    {
                        ["id"] = call.Id ?? string.Empty,
                        ["name"] = call.Name ?? string.Empty,
                        ["arguments"] = call.Arguments ?? string.Empty
                    });
                }
                obj["tool_calls"] = calls;
            }

            // Inline so the file is self-contained; the source image may be gone by load time.
            if (message.Image != null)
            {
                obj["image"] = message.Image;
                obj["image_mime"] = string.IsNullOrEmpty(message.ImageMime) ? "image/png" : message.ImageMime;
            }

            return obj;
        }
    }
}
