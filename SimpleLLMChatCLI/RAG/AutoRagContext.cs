using System;
using System.Collections.Generic;

namespace SimpleLLMChatCLI.RAG
{
    public enum AutoRagResultKind
    {
        Skipped,
        NoHits,
        Success
    }

    public sealed class AutoRagResult
    {
        public AutoRagResultKind Kind;
        public string MergedPrompt;
        public string UserStatusLine;
    }

    /// <summary>
    /// Automatic semantic retrieval for new chats / every turn.
    /// </summary>
    public static class AutoRagContext
    {
        public static AutoRagResult TryRetrieve(ConfigHandler config, string query, string baseUserPrompt)
        {
            AutoRagResult result = new AutoRagResult
            {
                Kind = AutoRagResultKind.Skipped,
                MergedPrompt = baseUserPrompt
            };

            if (config == null || !config.GetConfigBool("ragEnabled", false))
                return result;
            if (string.IsNullOrWhiteSpace(query))
                return result;

            DocumentIndex index = DocumentIndex.GetCurrent(config);
            if (!index.HasIndex)
            {
                // Empty knowledge / not indexed yet — same as a search with no hits.
                result.Kind = AutoRagResultKind.NoHits;
                return result;
            }

            IndexedHitSet hits = SemanticHitFormatter.Search(
                index, config, query, config.GetConfigInt("ragMaxResults", 5));
            if (hits == null || string.IsNullOrWhiteSpace(hits.FormattedText))
            {
                result.Kind = AutoRagResultKind.NoHits;
                return result;
            }

            string block = "---Retrieved Context---\n"
                + hits.FormattedText
                + "\n---End Retrieved Context---";

            result.Kind = AutoRagResultKind.Success;
            result.MergedPrompt = MergeIntoPrompt(baseUserPrompt, block);
            result.UserStatusLine = BuildReadingLine(hits.FilePaths);
            return result;
        }

        public static string MergeIntoPrompt(string basePrompt, string retrievedBlock)
        {
            const string separator = "\n\n---\n\n";
            string baseText = basePrompt ?? string.Empty;
            int idx = baseText.IndexOf(separator, StringComparison.Ordinal);
            if (idx >= 0)
            {
                string before = baseText.Substring(0, idx);
                string after = baseText.Substring(idx); // includes ---
                return before + "\n\n" + retrievedBlock + after;
            }
            return retrievedBlock + separator + baseText;
        }

        private static string BuildReadingLine(List<string> files)
        {
            if (files == null || files.Count == 0)
                return null;
            return "[reading " + string.Join(", ", files.ToArray()) + "]";
        }

    }
}
