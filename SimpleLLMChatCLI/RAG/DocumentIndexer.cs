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

    /// <summary>One chunk of a file: its line range and the text that is embedded.</summary>
    public struct IndexChunk
    {
        public int Start;
        public int End;
        public string Text;
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
            if (!_config.Get(AppSettings.RagEnabled))
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

            int chunkLines = _config.Get(AppSettings.IndexChunkLines);
            int overlap = _config.Get(AppSettings.IndexChunkOverlap);
            int maxSnippet = _config.Get(AppSettings.RagMaxSnippetLength);

            Func<string, string, IEnumerable<IndexChunk>> chunker = (file, content) =>
                LineChunks(index.ToDisplayPath(file), content, chunkLines, overlap, maxSnippet);
            bool changed;
            if (!ReconcileFiles(index, files, embeddings, fullRebuild, chunker, Report, out changed))
                return;

            index.Manifest.EmbeddingsModel = _config.Get(AppSettings.EmbeddingsModel);
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

        /// <summary>
        /// Brings <paramref name="index"/> in line with <paramref name="files"/>: chunks and
        /// embeds new or changed files (by mtime, then content hash), drops files no longer
        /// listed. Shared by the knowledge folder and chat history, which differ only in
        /// how a file is chunked. False (index left unsaved) when embedding fails.
        /// <paramref name="changed"/> is false when the index was already up to date, so
        /// there is nothing to save.
        /// </summary>
        public static bool ReconcileFiles(
            DocumentIndex index,
            IList<string> files,
            EmbeddingsClient embeddings,
            bool fullRebuild,
            Func<string, string, IEnumerable<IndexChunk>> chunker,
            Action<IndexProgress> report,
            out bool changed)
        {
            changed = fullRebuild;
            Action<IndexProgress> send = p => { if (report != null) report(p); };
            send(new IndexProgress { Phase = "start", Total = files.Count });

            var pendingTexts = new List<string>();
            var pendingChunks = new List<ChunkVector>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int current = 0;

            foreach (string file in files)
            {
                current++;
                seen.Add(file);
                send(new IndexProgress
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
                    {
                        existing.Mtime = mtime;
                        changed = true;
                    }
                    continue;
                }

                // The previous version's vectors by text hash: a chunk whose text is
                // unchanged keeps its embedding instead of being sent again.
                var previous = new Dictionary<string, float[]>(StringComparer.Ordinal);
                foreach (ChunkVector old in index.GetFileVectors(file))
                {
                    if (!string.IsNullOrEmpty(old.Hash))
                        previous[old.Hash] = old.Embedding;
                }

                // A blank file was never stored, so dropping it changes nothing.
                if (index.Manifest.Files.ContainsKey(file))
                    changed = true;
                index.RemoveFileData(file);

                if (string.IsNullOrWhiteSpace(content))
                    continue;

                foreach (IndexChunk chunk in chunker(file, content))
                {
                    if (string.IsNullOrWhiteSpace(chunk.Text))
                        continue;

                    ChunkVector vector = new ChunkVector
                    {
                        File = file,
                        StartLine = chunk.Start,
                        EndLine = chunk.End,
                        Hash = DocumentIndex.HashMd5Hex(chunk.Text, lowercase: false)
                    };
                    float[] embedding;
                    if (previous.TryGetValue(vector.Hash, out embedding))
                    {
                        vector.Embedding = embedding;
                        index.Vectors.Add(vector);
                        continue;
                    }
                    pendingChunks.Add(vector);
                    pendingTexts.Add(chunk.Text);
                }

                index.Manifest.Files[file] = new IndexFileEntry
                {
                    Hash = hash,
                    Mtime = mtime
                };
                changed = true;
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
            if (toRemove.Count > 0)
                changed = true;

            if (pendingTexts.Count > 0)
            {
                try
                {
                    send(new IndexProgress { Phase = "embedding", Current = 0, Total = pendingTexts.Count });
                    List<float[]> vectors = embeddings.EmbedBatch(pendingTexts, (done, total) =>
                        send(new IndexProgress { Phase = "embedding", Current = done, Total = total }));
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
                    send(new IndexProgress
                    {
                        Phase = "error",
                        Message = "Embeddings failed: " + ex.Message + " (index not saved)."
                    });
                    return false;
                }
            }
            return true;
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

        internal static string Truncate(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
                return text;
            return text.Substring(0, maxChars);
        }

        // Knowledge files: chunkLines-line windows with overlap, labelled with the file.
        // The label counts against maxSnippet, so the embedded text (label and lines) stays
        // within it; a label as long as the limit leaves no room, and the file has no chunks.
        private static IEnumerable<IndexChunk> LineChunks(
            string displayPath, string content, int chunkLines, int overlap, int maxSnippet)
        {
            string label = "File: " + displayPath + "\n";
            int budget = maxSnippet > 0 ? Math.Max(1, maxSnippet - label.Length) : 0;
            foreach (IndexChunk chunk in Chunk(content, chunkLines, overlap, budget))
            {
                yield return new IndexChunk
                {
                    Start = chunk.Start,
                    End = chunk.End,
                    Text = label + chunk.Text
                };
            }
        }

        private static IEnumerable<IndexChunk> Chunk(string content, int chunkLines, int overlap, int maxChars)
        {
            if (string.IsNullOrEmpty(content))
                yield break;

            string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (chunkLines < 1) chunkLines = 60;

            int start = 0;
            while (start < lines.Length)
            {
                // A line that alone exceeds maxChars fits in no chunk (chunks are line ranges),
                // so it is left out of the index; this keeps maxChars a hard limit on every chunk.
                if (maxChars > 0 && lines[start].Length + 6 > maxChars)
                {
                    start++;
                    continue;
                }

                // Close the chunk at chunkLines or at maxChars, whichever comes first, so the
                // snippet cap never cuts off lines that the next chunk does not cover.
                // Each line costs 6 extra: the indent and line break added when it is injected.
                int limit = Math.Min(start + chunkLines, lines.Length);
                int end = start;
                int used = 0;
                StringBuilder sb = new StringBuilder();
                while (end < limit)
                {
                    used += lines[end].Length + 6;
                    if (maxChars > 0 && used > maxChars)
                        break;
                    sb.AppendLine(lines[end]);
                    end++;
                }
                string text = sb.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return new IndexChunk
                    {
                        Start = start + 1,
                        End = end,
                        Text = text
                    };
                }
                if (end >= lines.Length)
                    break;

                // Step back by the overlap, unless the chunk was too short to allow it or it
                // stopped at an overlong line (stepping back would only repeat the overlap).
                bool nextIsOverlong = maxChars > 0 && lines[end].Length + 6 > maxChars;
                start = end - overlap > start && !nextIsOverlong ? end - overlap : end;
            }
        }
    }
}
