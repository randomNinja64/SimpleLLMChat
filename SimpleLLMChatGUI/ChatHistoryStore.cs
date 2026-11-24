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
        public DateTime Modified { get; set; }

        public string WhenText
        {
            get
            {
                return Modified.Date == DateTime.Today
                    ? Modified.ToShortTimeString()
                    : Modified.ToShortDateString();
            }
        }
    }

    /// <summary>Lists the saved sessions in the chats folder beside the program.</summary>
    public static class ChatHistoryStore
    {
        public static string ChatsDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chats"); }
        }

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
                using (StreamReader stream = new StreamReader(path, Encoding.UTF8))
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
