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
        private const int SnippetLines = 10;

        public static IndexedHitSet Search(DocumentIndex index, ConfigHandler config, string query, int maxResults)
        {
            if (index == null || !index.HasIndex)
                return null;

            EmbeddingsClient client = EmbeddingsClient.CreateFromConfig(config);
            if (client == null)
                return null;

            float[] queryVector;
            try { queryVector = client.Embed(query); }
            catch (EmbeddingsException) { return null; }
            if (queryVector == null)
                return null;

            List<ChunkHit> hits = index.Vectors.Search(queryVector, maxResults);
            if (hits == null || hits.Count == 0)
                return null;

            int maxSnippet = config != null ? config.GetConfigInt("ragMaxSnippetLength", 2000) : 2000;
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

                sb.AppendLine(string.Format("{0}:{1}-{2}  (score {3:F3})",
                    file, hit.Chunk.StartLine, hit.Chunk.EndLine, hit.Score));

                string snippet = ReadSnippet(file, hit.Chunk.StartLine, hit.Chunk.EndLine, maxSnippet);
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

            var header = new StringBuilder();
            header.AppendLine("Semantic search results for: " + query);
            header.AppendLine();

            return new IndexedHitSet
            {
                FormattedText = header.ToString() + sb.ToString().TrimEnd(),
                FilePaths = files
            };
        }

        private static string ReadSnippet(string file, int startLine, int endLine, int maxSnippet)
        {
            try
            {
                string[] lines = File.ReadAllLines(file);
                int from = Math.Max(1, startLine);
                int to = Math.Min(Math.Min(endLine, from + SnippetLines - 1), lines.Length);
                if (from > lines.Length)
                    return null;

                var sb = new StringBuilder();
                for (int i = from; i <= to; i++)
                    sb.AppendLine("    " + lines[i - 1]);
                if (endLine > to)
                    sb.AppendLine("    ...");

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
