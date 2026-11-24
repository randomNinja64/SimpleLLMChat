using System;
using System.Collections.Generic;

namespace SimpleLLMChatCLI.RAG
{
    public sealed class AutoRagResult
    {
        /// <summary>
        /// The retrieved block, stored on the user message apart from what was typed
        /// (see <see cref="AutoRagContext.MergeIntoPrompt"/>); null when nothing was retrieved.
        /// </summary>
        public string Context;

        /// <summary>The knowledge files read, e.g. "[reading notes.txt]"; null when none.</summary>
        public string UserStatusLine;

        /// <summary>How many earlier chats were recalled, e.g. "[recalling 3 earlier chats]"; null when none.</summary>
        public string ChatStatusLine;
    }

    /// <summary>
    /// Automatic semantic retrieval for new chats / every turn: the top matches from the
    /// knowledge folder (ragenabled) and from the workspace's earlier chats
    /// (ragchathistory), ragmaxresults of each, each listed with its file and lines.
    /// </summary>
    public static class AutoRagContext
    {
        private const string BlockStart = "---Retrieved Context---";
        private const string BlockEnd = "---End Retrieved Context---";
        private const string Separator = "\n\n---\n\n";

        public static AutoRagResult TryRetrieve(ConfigHandler config, string query)
        {
            return TryRetrieve(config, query, null);
        }

        /// <param name="chats">The workspace's chats to recall from; null for knowledge only.</param>
        public static AutoRagResult TryRetrieve(ConfigHandler config, string query, ChatRecallScope chats)
        {
            AutoRagResult result = new AutoRagResult();

            if (config == null || string.IsNullOrWhiteSpace(query))
                return result;

            DocumentIndex knowledge = null;
            if (config.Get(AppSettings.RagEnabled))
            {
                knowledge = DocumentIndex.GetCurrent(config);
                if (!knowledge.HasIndex)
                    knowledge = null;
            }
            bool recallChats = chats != null && IsChatRecallEnabled(config);
            if (knowledge == null && !recallChats)
                return result;

            // One embedding of the query serves both searches.
            float[] queryVector = SemanticHitFormatter.EmbedQuery(config, query);
            if (queryVector == null)
                return result;

            int maxResults = config.Get(AppSettings.RagMaxResults);
            int maxSnippet = config.Get(AppSettings.RagMaxSnippetLength);
            List<string> sections = new List<string>();

            if (knowledge != null)
            {
                IndexedHitSet hits = SemanticHitFormatter.FormatHits(
                    knowledge, queryVector, maxResults, maxSnippet, SemanticHitFormatter.ReadSnippet);
                if (hits != null)
                {
                    sections.Add("Semantic search results for: " + query + Environment.NewLine
                        + Environment.NewLine + hits.FormattedText);
                    result.UserStatusLine = "[reading " + string.Join(", ", hits.FilePaths.ToArray()) + "]";
                }
            }

            if (recallChats)
            {
                IndexedHitSet hits = ChatHistoryRag.Recall(config, chats, queryVector, maxResults);
                if (hits != null)
                {
                    sections.Add("Earlier chats in this workspace matching: " + query + Environment.NewLine
                        + Environment.NewLine + hits.FormattedText);
                    int count = hits.FilePaths.Count;
                    result.ChatStatusLine = "[recalling " + count + " earlier chat" + (count == 1 ? "" : "s") + "]";
                }
            }

            if (sections.Count == 0)
                return result;

            result.Context = BlockStart + "\n"
                + string.Join(Environment.NewLine + Environment.NewLine, sections.ToArray())
                + "\n" + BlockEnd;
            return result;
        }

        /// <summary>Recall needs history kept (chathistorylimit) and the RAG option on.</summary>
        public static bool IsChatRecallEnabled(ConfigHandler config)
        {
            return config.Get(AppSettings.RagChatHistory) && config.Get(AppSettings.ChatHistoryLimit) > 0;
        }

        /// <summary>
        /// The text the model reads for a user message with retrieved context: the
        /// context, a separator, then the prompt (or, when the prompt already separates
        /// off a preamble, the context goes after the preamble).
        /// </summary>
        public static string MergeIntoPrompt(string retrievedBlock, string basePrompt)
        {
            string baseText = basePrompt ?? string.Empty;
            int idx = baseText.IndexOf(Separator, StringComparison.Ordinal);
            if (idx >= 0)
            {
                string before = baseText.Substring(0, idx);
                string after = baseText.Substring(idx); // includes ---
                return before + "\n\n" + retrievedBlock + after;
            }
            return retrievedBlock + Separator + baseText;
        }
    }
}
