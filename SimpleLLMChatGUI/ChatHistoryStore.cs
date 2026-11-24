using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace SimpleLLMChatGUI
{
    /// <summary>One saved session in the history sidebar.</summary>
    public sealed class ChatHistoryEntry
    {
        public string Path { get; set; }
        public string Title { get; set; }

        // Last save time: orders the list newest first. Not shown.
        public DateTime Modified { get; set; }
    }

    /// <summary>Lists a workspace's saved sessions (see <see cref="ChatWorkspace"/>).</summary>
    public static class ChatHistoryStore
    {
        /// <summary>Saved sessions in <paramref name="directory"/>, newest first.</summary>
        public static List<ChatHistoryEntry> Read(string directory)
        {
            List<ChatHistoryEntry> entries = new List<ChatHistoryEntry>();
            if (!Directory.Exists(directory))
                return entries;

            foreach (string path in Directory.GetFiles(directory, "*.json"))
            {
                entries.Add(new ChatHistoryEntry
                {
                    Path = path,
                    Title = ReadTitle(path) ?? Path.GetFileNameWithoutExtension(path),
                    Modified = File.GetLastWriteTime(path)
                });
            }

            entries.Sort((a, b) => b.Modified.CompareTo(a.Modified));
            return entries;
        }

        /// <summary>
        /// Reads only the top-level "title" property. The CLI writes it first, so
        /// the rest of the file (which can hold inline images) is never parsed.
        /// </summary>
        private static string ReadTitle(string path)
        {
            try
            {
                // Share everything, Delete included: the CLI's save swaps a new file
                // over this one, and a plain open would block that swap.
                using (FileStream file = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader stream = new StreamReader(file, Encoding.UTF8))
                using (JsonTextReader reader = new JsonTextReader(stream))
                {
                    if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                        return null;

                    while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
                    {
                        if ((string)reader.Value == "title")
                        {
                            string title = reader.ReadAsString();
                            return string.IsNullOrWhiteSpace(title) ? null : title;
                        }
                        reader.Skip();
                    }
                }
            }
            catch (Exception)
            {
                // Unreadable or partly written: list it under its file name.
            }
            return null;
        }
    }
}
