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

            int maxSnippet = config != null ? config.Get(AppSettings.RagMaxSnippetLength) : 2000;
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
