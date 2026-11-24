using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SimpleLLMChatCLI
{
    /// <summary>
    /// The saved-chat file format: { "title": ..., "workingDirectory": ..., "messages": [...] }. The
    /// messages record the conversation exactly as it is sent to the model
    /// (roles, tool calls, tool results, images), so a later loader can rebuild
    /// the ChatMessage list and continue the session.
    /// </summary>
    internal static class ChatTranscript
    {
        private const int TitleMaxLength = 60;

        public static void Save(string path, string title, string workingDirectory, List<LLMClient.ChatMessage> conversation)
        {
            JArray messages = new JArray();
            foreach (LLMClient.ChatMessage message in conversation)
                messages.Add(ToJson(message));

            // Small fields first: the GUI's list reads the title without parsing the rest.
            JObject root = new JObject();
            if (!string.IsNullOrEmpty(title))
                root["title"] = title;
            if (!string.IsNullOrEmpty(workingDirectory))
                root["workingDirectory"] = workingDirectory;
            root["messages"] = messages;

            WriteAtomically(path, new UTF8Encoding(false).GetBytes(root.ToString(Formatting.Indented) + "\n"));
        }

        /// <summary>
        /// Writes beside the target, flushes to disk, then swaps it into place, so
        /// the file is always the old or the new version, never a partial one. The
        /// GUI kills the CLI on New Chat, which can land mid-save, and a session is
        /// rewritten whole every turn: a plain overwrite would lose all of it.
        /// </summary>
        private static void WriteAtomically(string path, byte[] contents)
        {
            string target = Path.GetFullPath(path);
            string temp = target + ".tmp";

            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents, 0, contents.Length);
                stream.Flush(true);
            }

            // A reader (the GUI's sidebar, antivirus) can hold the target open for
            // a moment and block the swap; retry briefly before giving up.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(target))
                        File.Replace(temp, target, null);
                    else
                        File.Move(temp, target);
                    return;
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException) || attempt >= 5)
                    {
                        try { File.Delete(temp); } catch (Exception) { }
                        throw;
                    }
                    Thread.Sleep(50 * attempt);
                }
            }
        }

        public static List<LLMClient.ChatMessage> Load(string path, out string title, out string workingDirectory)
        {
            JToken root = JToken.Parse(File.ReadAllText(path, Encoding.UTF8));

            // Early saves were a bare message array with no title.
            JArray messages = root as JArray;
            title = null;
            workingDirectory = null;
            if (messages == null)
            {
                title = (string)root["title"];
                workingDirectory = (string)root["workingDirectory"];
                messages = root["messages"] as JArray ?? new JArray();
            }

            List<LLMClient.ChatMessage> conversation = new List<LLMClient.ChatMessage>();
            foreach (JObject obj in messages)
            {
                LLMClient.ChatMessage message = new LLMClient.ChatMessage(
                    (string)obj["role"] ?? string.Empty, (string)obj["content"] ?? string.Empty);
                message.ToolCallId = (string)obj["tool_call_id"] ?? string.Empty;
                message.Image = (string)obj["image"];
                message.ImageMime = (string)obj["image_mime"];

                JArray calls = obj["tool_calls"] as JArray;
                if (calls != null)
                {
                    foreach (JObject call in calls)
                    {
                        message.ToolCalls.Add(new ToolRegistry.ToolCall
                        {
                            Id = (string)call["id"] ?? string.Empty,
                            Name = (string)call["name"] ?? string.Empty,
                            Arguments = (string)call["arguments"] ?? string.Empty
                        });
                    }
                }
                conversation.Add(message);
            }

            if (string.IsNullOrEmpty(title))
                title = TitleFromConversation(conversation);
            return conversation;
        }

        /// <summary>
        /// A one-line title from the prompt the user typed: whitespace collapsed,
        /// cut at a word boundary. Null when there is no text (an image-only message).
        /// </summary>
        public static string MakeTitle(string prompt)
        {
            string text = Regex.Replace(prompt ?? string.Empty, @"\s+", " ").Trim();
            if (text.Length == 0)
                return null;
            if (text.Length <= TitleMaxLength)
                return text;

            int cut = text.LastIndexOf(' ', TitleMaxLength);
            if (cut < TitleMaxLength / 2)
                cut = TitleMaxLength;
            return text.Substring(0, cut).TrimEnd() + "...";
        }

        // For files without a title: the first user message. RAG context or a
        // summary may have been merged into it, but it is the best there is.
        private static string TitleFromConversation(List<LLMClient.ChatMessage> conversation)
        {
            foreach (LLMClient.ChatMessage message in conversation)
            {
                if (message.Role == "user")
                    return MakeTitle(message.Content);
            }
            return null;
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
