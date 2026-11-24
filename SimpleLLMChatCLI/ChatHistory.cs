using System;
using System.Globalization;
using System.IO;

namespace SimpleLLMChatCLI
{
    /// <summary>
    /// The auto-saved sessions, one folder per workspace (see <see cref="ChatWorkspace"/>):
    /// naming a new session's file, and pruning a workspace's oldest beyond the limit.
    /// </summary>
    internal static class ChatHistory
    {
        /// <summary>A file name from the start time, e.g. 20261123-221530.json.</summary>
        public static string NewSessionPath(string workspace)
        {
            string directory = ChatWorkspace.DirectoryFor(workspace);
            string stem = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, stem + ".json");
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(directory, stem + "-" + n.ToString(CultureInfo.InvariantCulture) + ".json");
            return path;
        }

        /// <summary>
        /// Deletes the oldest sessions in <paramref name="currentPath"/>'s folder beyond
        /// <paramref name="keep"/>, never the current one: the limit is per workspace.
        /// </summary>
        public static void Prune(int keep, string currentPath)
        {
            DirectoryInfo directory = new DirectoryInfo(Path.GetDirectoryName(currentPath));
            // Only workspace folders under chats\: a chat /load'ed from elsewhere
            // saves back to its own folder, which is never pruned.
            if (!directory.Exists || directory.Parent == null
                || !ChatWorkspace.SamePath(directory.Parent.FullName, ChatWorkspace.ChatsRoot))
                return;

            FileInfo[] sessions = directory.GetFiles("*.json");
            Array.Sort(sessions, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

            int kept = 0;
            foreach (FileInfo session in sessions)
            {
                bool current = string.Equals(session.FullName, currentPath, StringComparison.OrdinalIgnoreCase);
                if (current || kept < keep)
                {
                    kept++;
                    continue;
                }
                TryDelete(session);
            }
        }

        private static void TryDelete(FileInfo file)
        {
            try { file.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
