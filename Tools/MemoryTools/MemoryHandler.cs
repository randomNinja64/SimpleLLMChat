using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MemoryTools
{
    internal static class MemoryHandler
    {
        private static readonly string memoryFolder = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? typeof(MemoryHandler).Assembly.Location), "memories");

        private const int MaxNameLength = 100;

        // Converts a name to a safe filename by replacing any character that
        // isn't alphanumeric, underscore, or hyphen with an underscore.
        private static string NameToFileName(string name)
        {
            string trimmed = name.Trim();
            if (trimmed.Length > MaxNameLength)
                throw new ArgumentException($"name must be {MaxNameLength} characters or fewer (got {trimmed.Length}).");
            string safe = Regex.Replace(trimmed, @"[^a-zA-Z0-9_\-]", "_");
            if (string.IsNullOrEmpty(safe))
                throw new ArgumentException("name produces an empty filename after sanitization.");
            return safe + ".md";
        }

        public static string SaveMemory(string name, string content)
        {
            int maxContentLength = ToolHelper.GetConfigInt("maxContentLength", 2000);
            bool truncated = content.Length > maxContentLength;
            if (truncated)
                content = content.Substring(0, maxContentLength);

            if (!Directory.Exists(memoryFolder))
                Directory.CreateDirectory(memoryFolder);

            string path = Path.Combine(memoryFolder, NameToFileName(name));
            bool exists = File.Exists(path);

            var evicted = new System.Collections.Generic.List<string>();
            if (!exists)
            {
                string[] existing = Directory.GetFiles(memoryFolder, "*.md");
                int maxMemories = ToolHelper.GetConfigInt("maxMemories", 50);
                if (existing.Length >= maxMemories)
                {
                    Array.Sort(existing, (a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
                    for (int i = 0; i <= existing.Length - maxMemories; i++)
                    {
                        evicted.Add(Path.GetFileNameWithoutExtension(existing[i]));
                        File.Delete(existing[i]);
                    }
                }
            }

            File.WriteAllText(path, content, Encoding.UTF8);

            var sb = new StringBuilder();
            sb.Append(exists ? "Memory updated: " + name : "Memory saved: " + name);
            if (truncated)
                sb.Append(" (content truncated to " + maxContentLength + " characters)");
            if (evicted.Count > 0)
                sb.Append(" (evicted oldest: " + string.Join(", ", evicted) + ")");
            return sb.ToString();
        }

        public static string RecallMemory(string name)
        {
            string path = Path.Combine(memoryFolder, NameToFileName(name));
            if (!File.Exists(path))
                return "No memory entry found with name: " + name;

            return File.ReadAllText(path, Encoding.UTF8).Trim();
        }

        public static string DeleteMemory(string name)
        {
            string path = Path.Combine(memoryFolder, NameToFileName(name));
            if (!File.Exists(path))
                return "No memory entry found with name: " + name;

            File.Delete(path);
            return "Memory deleted: " + name;
        }

        public static string ListMemories()
        {
            if (!Directory.Exists(memoryFolder))
                return "(no memories saved)";

            string[] files = Directory.GetFiles(memoryFolder, "*.md");
            if (files.Length == 0)
                return "(no memories saved)";

            Array.Sort(files);

            StringBuilder sb = new StringBuilder();
            foreach (string file in files)
                sb.AppendLine(Path.GetFileNameWithoutExtension(file));

            return sb.ToString().Trim();
        }

        public static string GetContext()
        {
            if (!Directory.Exists(memoryFolder))
                return null;

            string[] files = Directory.GetFiles(memoryFolder, "*.md");
            if (files.Length == 0)
                return null;

            Array.Sort(files);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Current Memories:");

            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string content = File.ReadAllText(file, Encoding.UTF8).Trim();
                string preview = content.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                if (preview.Length > 150)
                    preview = preview.Substring(0, 150) + "...";
                sb.AppendLine(name + ": " + preview);
            }

            return sb.ToString().Trim();
        }

        public static string SearchMemories(string keyword)
        {
            if (!Directory.Exists(memoryFolder))
                return "(no memories saved)";

            string[] files = Directory.GetFiles(memoryFolder, "*.md");
            if (files.Length == 0)
                return "(no memories saved)";

            Array.Sort(files);

            StringBuilder sb = new StringBuilder();
            foreach (string file in files)
            {
                string content = File.ReadAllText(file, Encoding.UTF8);
                if (content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string name = Path.GetFileNameWithoutExtension(file);
                sb.AppendLine("## " + name);

                string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (string line in lines)
                {
                    if (line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        sb.AppendLine(line);
                }
                sb.AppendLine();
            }

            string result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "No memories matched: " + keyword : result;
        }
    }
}
