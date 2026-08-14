using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SimpleLLMChatCLI.RAG
{
    public sealed class IndexProgress
    {
        public string Phase;
        public int Current;
        public int Total;
        public int FileCount;
        public string File;
        public string Message;
    }

    /// <summary>
    /// Incremental knowledge-folder indexer: extract, chunk, embed, persist.
    /// Scheduling / single-run serialization lives in <see cref="RagHost"/>.
    /// </summary>
    public class DocumentIndexer
    {
        private readonly ConfigHandler _config;
        private readonly Action<IndexProgress> _onProgress;

        public DocumentIndexer(ConfigHandler config, Action<IndexProgress> onProgress)
        {
            _config = config;
            _onProgress = onProgress;
        }

        public void Reconcile()
        {
            if (!_config.GetConfigBool("ragEnabled", false))
            {
                Report(new IndexProgress { Phase = "error", Message = "RAG is disabled." });
                return;
            }

            string knowledgePath = DocumentIndex.ResolveKnowledgePath(_config);
            if (!Directory.Exists(knowledgePath))
            {
                try { Directory.CreateDirectory(knowledgePath); }
                catch (Exception ex)
                {
                    Report(new IndexProgress { Phase = "error", Message = "Cannot create knowledge folder: " + ex.Message });
                    return;
                }
            }

            EmbeddingsClient embeddings = EmbeddingsClient.CreateFromConfig(_config);
            if (embeddings == null)
            {
                Report(new IndexProgress
                {
                    Phase = "error",
                    Message = "Embeddings model is required when RAG is enabled."
                });
                return;
            }

            DocumentIndex index = DocumentIndex.LoadFor(knowledgePath);
            bool fullRebuild = index.ParamsMismatch(_config);
            if (fullRebuild)
            {
                index.Vectors.Clear();
                index.Manifest.Files.Clear();
            }

            List<string> files = new List<string>(DocumentScanFilter.EnumerateFiles(
                knowledgePath, DocumentScanFilter.GetAllowedExtensions(_config)));
            Report(new IndexProgress { Phase = "start", Total = files.Count });

            int chunkLines = _config.GetConfigInt("indexChunkLines", 60);
            int overlap = _config.GetConfigInt("indexChunkOverlap", 10);
            int maxSnippet = _config.GetConfigInt("ragMaxSnippetLength", 2000);

            var pendingTexts = new List<string>();
            var pendingChunks = new List<ChunkVector>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int current = 0;

            foreach (string file in files)
            {
                current++;
                seen.Add(file);
                Report(new IndexProgress
                {
                    Phase = "progress",
                    Current = current,
                    Total = files.Count,
                    File = Path.GetFileName(file)
                });

                IndexFileEntry existing = null;
                long mtime = SafeMtime(file);
                // Skip unchanged files without reading when mtime matches.
                if (!fullRebuild
                    && mtime != 0
                    && index.Manifest.Files.TryGetValue(file, out existing)
                    && existing != null
                    && existing.Mtime == mtime)
                {
                    continue;
                }

                string content;
                string hash;
                if (!TryReadContent(file, out content, out hash))
                    continue;

                if (!fullRebuild
                    && index.Manifest.Files.TryGetValue(file, out existing)
                    && existing != null
                    && string.Equals(existing.Hash, hash, StringComparison.Ordinal))
                {
                    // Content identical; refresh stored mtime so the next reconcile can skip the read.
                    if (existing.Mtime != mtime)
                        existing.Mtime = mtime;
                    continue;
                }

                index.RemoveFileData(file);

                if (string.IsNullOrWhiteSpace(content))
                    continue;

                string relative = index.ToDisplayPath(file);
                foreach (ChunkInfo ci in Chunk(content, chunkLines, overlap))
                {
                    string text = Truncate(ci.Text, maxSnippet);
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    ChunkVector cv = new ChunkVector
                    {
                        File = file,
                        StartLine = ci.Start,
                        EndLine = ci.End
                    };
                    pendingChunks.Add(cv);
                    pendingTexts.Add("File: " + relative + "\n" + text);
                }

                index.Manifest.Files[file] = new IndexFileEntry
                {
                    Hash = hash,
                    Mtime = mtime
                };
            }

            // Prune removed files
            List<string> toRemove = new List<string>();
            foreach (string key in index.Manifest.Files.Keys)
            {
                if (!seen.Contains(key))
                    toRemove.Add(key);
            }
            foreach (string key in toRemove)
                index.RemoveFileData(key);

            if (pendingTexts.Count > 0)
            {
                try
                {
                    List<float[]> vectors = embeddings.EmbedBatch(pendingTexts);
                    for (int i = 0; i < pendingChunks.Count; i++)
                    {
                        pendingChunks[i].Embedding = vectors[i];
                        index.Vectors.Add(pendingChunks[i]);
                    }
                }
                catch (EmbeddingsException ex)
                {
                    // Discard this pass: hashes were stamped before embed, so saving would
                    // skip those files forever on the next reconcile.
                    Report(new IndexProgress
                    {
                        Phase = "error",
                        Message = "Embeddings failed: " + ex.Message + " (index not saved)."
                    });
                    return;
                }
            }

            index.Manifest.EmbeddingsModel = _config.GetConfigValue("embeddingsModel");
            index.Manifest.IndexChunkLines = chunkLines;
            index.Manifest.IndexChunkOverlap = overlap;
            index.Manifest.RagMaxSnippetLength = maxSnippet;
            index.Save();
            DocumentIndex.SetCurrent(index);

            Report(new IndexProgress
            {
                Phase = "done",
                FileCount = index.Manifest.Files.Count
            });
        }

        public void ClearIndex()
        {
            string knowledgePath = DocumentIndex.ResolveKnowledgePath(_config);
            DocumentIndex index = DocumentIndex.LoadFor(knowledgePath);
            index.Clear();
            DocumentIndex.SetCurrent(index);
            Report(new IndexProgress { Phase = "cleared" });
        }

        private void Report(IndexProgress progress)
        {
            Action<IndexProgress> handler = _onProgress;
            if (handler != null)
                handler(progress);
        }

        private static bool TryReadContent(string file, out string content, out string hash)
        {
            content = null;
            hash = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                if (DocumentScanFilter.LooksBinary(bytes, DocumentScanFilter.BinarySampleBytes))
                    return false;
                content = DecodeText(bytes);
                hash = DocumentIndex.HashMd5Hex(content, lowercase: false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string DecodeText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            // Honor a UTF-8 BOM; otherwise decode as UTF-8 (lenient).
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            return new UTF8Encoding(false).GetString(bytes);
        }

        private static long SafeMtime(string file)
        {
            try { return File.GetLastWriteTimeUtc(file).Ticks; }
            catch { return 0; }
        }

        private static string Truncate(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
                return text;
            return text.Substring(0, maxChars);
        }

        private struct ChunkInfo
        {
            public int Start;
            public int End;
            public string Text;
        }

        private static IEnumerable<ChunkInfo> Chunk(string content, int chunkLines, int overlap)
        {
            if (string.IsNullOrEmpty(content))
                yield break;

            string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (chunkLines < 1) chunkLines = 60;
            int step = chunkLines - overlap;
            if (step < 1) step = chunkLines;

            for (int start = 0; start < lines.Length; start += step)
            {
                int end = Math.Min(start + chunkLines, lines.Length);
                StringBuilder sb = new StringBuilder();
                for (int i = start; i < end; i++)
                    sb.AppendLine(lines[i]);
                string text = sb.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return new ChunkInfo
                    {
                        Start = start + 1,
                        End = end,
                        Text = text
                    };
                }
                if (end >= lines.Length)
                    break;
            }
        }
    }
}
