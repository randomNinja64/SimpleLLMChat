using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SimpleLLMChatCLI.RAG
{
    public sealed class IndexedHitSet
    {
        public string FormattedText;
        public List<string> FilePaths;
    }

    public static class SemanticHitFormatter
    {
        public static IndexedHitSet Search(DocumentIndex index, ConfigHandler config, string query, int maxResults)
        {
            if (index == null || !index.HasIndex)
                return null;

            float[] queryVector = EmbedQuery(config, query);
            if (queryVector == null)
                return null;

            int maxSnippet = config != null ? config.Get(AppSettings.RagMaxSnippetLength) : 2000;
            IndexedHitSet hits = FormatHits(index, queryVector, maxResults, maxSnippet, ReadSnippet);
            if (hits == null)
                return null;

            hits.FormattedText = "Semantic search results for: " + query + Environment.NewLine
                + Environment.NewLine + hits.FormattedText;
            return hits;
        }

        /// <summary>The query's embedding, or null when embeddings are unavailable.</summary>
        public static float[] EmbedQuery(ConfigHandler config, string query)
        {
            EmbeddingsClient client = EmbeddingsClient.CreateFromConfig(config);
            if (client == null)
                return null;
            try { return client.Embed(query); }
            catch (EmbeddingsException) { return null; }
        }

        /// <summary>
        /// The top hits as "path:start-end  (score)" followed by the text
        /// <paramref name="readSnippet"/> gives for that range (file, start, end, max
        /// length; null shows the location alone). Hits whose file is gone are skipped.
        /// </summary>
        public static IndexedHitSet FormatHits(
            DocumentIndex index,
            float[] queryVector,
            int maxResults,
            int maxSnippet,
            Func<string, int, int, int, string> readSnippet)
        {
            List<ChunkHit> hits = index.Vectors.Search(queryVector, maxResults);
            if (hits == null || hits.Count == 0)
                return null;

            var sb = new StringBuilder();
            var files = new List<string>();
            int shown = 0;

            foreach (ChunkHit hit in hits)
            {
                if (shown >= maxResults)
                    break;
                if (hit == null || hit.Chunk == null)
                    continue;

                string file = hit.Chunk.File;
                if (string.IsNullOrEmpty(file) || !File.Exists(file))
                    continue;

                string snippet = readSnippet(file, hit.Chunk.StartLine, hit.Chunk.EndLine, maxSnippet);

                sb.AppendLine(string.Format("{0}:{1}-{2}  (score {3:F3})",
                    file, hit.Chunk.StartLine, hit.Chunk.EndLine, hit.Score));
                if (!string.IsNullOrEmpty(snippet))
                    sb.AppendLine(snippet);
                sb.AppendLine();

                string name = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(name) && !files.Contains(name))
                    files.Add(name);
                shown++;
            }

            if (shown == 0)
                return null;

            return new IndexedHitSet
            {
                FormattedText = sb.ToString().TrimEnd(),
                FilePaths = files
            };
        }

        public static string ReadSnippet(string file, int startLine, int endLine, int maxSnippet)
        {
            try
            {
                // The whole indexed chunk; ragMaxSnippetLength bounds it below,
                // the same limit applied to the text that was embedded.
                int from = Math.Max(1, startLine);
                int to = endLine;

                var sb = new StringBuilder();
                int lineNum = 0;

                using (StreamReader reader = new StreamReader(file, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lineNum++;
                        if (lineNum < from)
                            continue;
                        if (lineNum > to)
                            break;

                        sb.AppendLine("    " + line);
                    }
                }

                if (lineNum < from)
                    return null;

                string result = sb.ToString().TrimEnd();
                if (maxSnippet > 0 && result.Length > maxSnippet)
                    result = result.Substring(0, maxSnippet);
                return result;
            }
            catch
            {
                return null;
            }
        }
    }
}
