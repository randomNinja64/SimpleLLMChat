using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SimpleLLMChatCLI.RAG
{
    /// <summary>Where chat recall looks: a workspace's chats folder, minus the open chat.</summary>
    public sealed class ChatRecallScope
    {
        public string Directory;

        /// <summary>The open chat's file (already in the conversation), or null.</summary>
        public string CurrentPath;
    }

    /// <summary>
    /// Retrieval over a workspace's saved chats, on the knowledge index's machinery
    /// (<see cref="DocumentIndexer.ReconcileFiles"/>, <see cref="DocumentIndex"/>):
    /// one chunk per exchange (a user message and the replies to it), tool output and
    /// images left out, located by its lines in the chat's JSON file.
    /// </summary>
    public static class ChatHistoryRag
    {
        private static readonly object Gate = new object();
        private static DocumentIndex _cached;

        /// <summary>
        /// Brings the workspace's chat index up to date and returns the top matches, or
        /// null when there are none or embeddings are unavailable.
        /// </summary>
        public static IndexedHitSet Recall(ConfigHandler config, ChatRecallScope scope, float[] queryVector, int maxResults)
        {
            if (scope == null || string.IsNullOrEmpty(scope.Directory) || !System.IO.Directory.Exists(scope.Directory))
                return null;

            EmbeddingsClient embeddings = EmbeddingsClient.CreateFromConfig(config);
            if (embeddings == null)
                return null;

            int maxSnippet = config.Get(AppSettings.RagMaxSnippetLength);
            DocumentIndex index;
            lock (Gate)
            {
                index = _cached != null && ChatWorkspace.SamePath(_cached.KnowledgePath, scope.Directory)
                    ? _cached
                    : DocumentIndex.LoadFor(ChatWorkspace.Normalize(scope.Directory));

                bool fullRebuild = index.ParamsMismatch(config);
                if (fullRebuild)
                {
                    index.Vectors.Clear();
                    index.Manifest.Files.Clear();
                }

                // The open chat is already in the conversation; leaving it out also keeps
                // it from being re-embedded after every turn.
                List<string> files = new List<string>();
                foreach (string file in System.IO.Directory.GetFiles(scope.Directory, "*.json"))
                {
                    string path = DocumentScanFilter.NormalizePath(file);
                    if (!ChatWorkspace.SamePath(path, scope.CurrentPath))
                        files.Add(path);
                }

                bool changed;
                if (!DocumentIndexer.ReconcileFiles(index, files, embeddings, fullRebuild,
                        (file, content) => ExchangeChunks(content, maxSnippet), null, out changed))
                {
                    _cached = null;   // Unsaved and part-updated: reload next time.
                    return null;
                }

                // Save only when this pass changed the index: most recalls find every chat
                // unchanged.
                if (changed)
                {
                    index.Manifest.EmbeddingsModel = config.Get(AppSettings.EmbeddingsModel);
                    index.Manifest.IndexChunkLines = config.Get(AppSettings.IndexChunkLines);
                    index.Manifest.IndexChunkOverlap = config.Get(AppSettings.IndexChunkOverlap);
                    index.Manifest.RagMaxSnippetLength = maxSnippet;
                    try { index.Save(); }
                    catch (IOException) { }   // Searchable now; saved with the next change.
                }
                _cached = index;
            }

            if (!index.HasIndex)
                return null;
            return SemanticHitFormatter.FormatHits(index, queryVector, maxResults, maxSnippet, ReadExchange);
        }

        /// <summary>
        /// One chunk per exchange of a saved chat: from a user message to the line
        /// before the next one. The text is the chat's title, the user's words and the
        /// assistant's replies; retrieved context (stored apart, in "context"), tool
        /// calls, tool output and images are left out.
        /// </summary>
        public static IEnumerable<IndexChunk> ExchangeChunks(string content, int maxSnippet)
        {
            JObject root;
            try
            {
                using (JsonTextReader reader = new JsonTextReader(new StringReader(content)))
                    root = JObject.Load(reader, new JsonLoadSettings { LineInfoHandling = LineInfoHandling.Load });
            }
            catch (JsonException)
            {
                yield break;   // Not a chat file.
            }

            JArray messages = root["messages"] as JArray;
            if (messages == null)
                yield break;

            string title = (string)root["title"];
            // The file ends "  ]\n}\n": the last message ends two lines before the end.
            int lastLine = Math.Max(1, content.TrimEnd('\n', '\r').Split('\n').Length - 2);

            int start = 0;
            StringBuilder text = null;
            foreach (JToken message in messages)
            {
                JObject obj = message as JObject;
                if (obj == null)
                    continue;
                string role = (string)obj["role"];
                string said = (string)obj["content"];
                int line = ((IJsonLineInfo)obj).LineNumber;

                if (role == "user")
                {
                    if (text != null)
                        yield return Exchange(start, line - 1, text, maxSnippet);
                    start = line;
                    text = new StringBuilder();
                    if (!string.IsNullOrEmpty(title))
                        text.Append("Chat: ").Append(title).Append('\n');
                    text.Append("User: ").Append(said).Append('\n');
                }
                else if (role == "assistant" && text != null && !string.IsNullOrWhiteSpace(said))
                {
                    text.Append("Assistant: ").Append(said.Trim()).Append('\n');
                }
            }
            if (text != null)
                yield return Exchange(start, Math.Max(start, lastLine), text, maxSnippet);
        }

        private static IndexChunk Exchange(int start, int end, StringBuilder text, int maxSnippet)
        {
            return new IndexChunk
            {
                Start = start,
                End = Math.Max(start, end),
                Text = DocumentIndexer.Truncate(text.ToString().TrimEnd(), maxSnippet)
            };
        }

        // A hit's text: the exchange re-read from the chat file, indented like a
        // knowledge snippet. The file and lines in the header stay usable by read and
        // grep tools; the text itself skips the JSON around it.
        private static string ReadExchange(string file, int startLine, int endLine, int maxSnippet)
        {
            try
            {
                string content = File.ReadAllText(file, Encoding.UTF8);
                foreach (IndexChunk chunk in ExchangeChunks(content, maxSnippet))
                {
                    if (chunk.Start != startLine)
                        continue;
                    StringBuilder sb = new StringBuilder();
                    foreach (string line in chunk.Text.Split('\n'))
                        sb.AppendLine("    " + line);
                    return sb.ToString().TrimEnd();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;   // Changed since indexing: skipped until the next reconcile.
        }
    }
}
