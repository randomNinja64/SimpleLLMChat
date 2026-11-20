using SimpleLLMChatCLI.RAG;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SimpleLLMChatCLI.Tests
{
    /// <summary>
    /// A knowledge folder, a settings file and a fake embeddings server.
    /// The index itself lives beside the test EXE and is removed on dispose.
    /// </summary>
    internal sealed class RagFixture : IDisposable
    {
        public readonly TempWorkspace Ws = new TempWorkspace("cli-rag");
        public readonly FakeOpenAiServer Server = new FakeOpenAiServer();
        public readonly List<IndexProgress> Progress = new List<IndexProgress>();
        public readonly string Knowledge;

        public RagFixture()
        {
            Knowledge = Ws.Combine("knowledge");
            Directory.CreateDirectory(Knowledge);
        }

        public ConfigHandler Config(string extraLines = "")
        {
            string path = Ws.Combine("rag.ini");
            File.WriteAllText(path,
                "[RAG]\r\n" +
                "ragenabled=1\r\n" +
                "ragknowledgepath=" + Knowledge + "\r\n" +
                "embeddingsmodel=test-embed\r\n" +
                "llmserver=" + Server.BaseUrl + "/v1\r\n" +
                "apikey=k\r\n" +
                extraLines,
                Encoding.UTF8);
            return new ConfigHandler(path);
        }

        public string WriteFile(string relativePath, string content)
        {
            string path = Path.Combine(Knowledge, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        public static string Lines(string prefix, int count)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i <= count; i++)
                sb.Append(prefix + i.ToString("000", CultureInfo.InvariantCulture) + "\r\n");
            return sb.ToString();
        }

        /// <summary>Runs one reconcile pass and returns the phase it ended on.</summary>
        public string Reconcile(string extraLines = "")
        {
            Progress.Clear();
            new DocumentIndexer(Config(extraLines), p => Progress.Add(p)).Reconcile();
            return Progress.Count > 0 ? Progress[Progress.Count - 1].Phase : "";
        }

        public string LastMessage
        {
            get { return Progress.Count > 0 ? (Progress[Progress.Count - 1].Message ?? "") : ""; }
        }

        /// <summary>The index as a fresh process would load it.</summary>
        public DocumentIndex OnDisk()
        {
            return DocumentIndex.LoadFor(DocumentIndex.ResolveKnowledgePath(Config()));
        }

        /// <summary>Start lines of the stored chunks of one file, ascending.</summary>
        public List<int> ChunkStarts(string fileName)
        {
            List<int> starts = new List<int>();
            foreach (ChunkHit hit in OnDisk().Vectors.Search(new float[] { 1f, 0f }, 0))
            {
                if (string.Equals(Path.GetFileName(hit.Chunk.File), fileName, StringComparison.OrdinalIgnoreCase))
                    starts.Add(hit.Chunk.StartLine);
            }
            starts.Sort();
            return starts;
        }

        public void Dispose()
        {
            try
            {
                string indexDir = OnDisk().IndexDir;
                if (Directory.Exists(indexDir))
                    Directory.Delete(indexDir, true);
                string root = DocumentIndex.GetIndexRootDir();
                if (Directory.Exists(root) && Directory.GetFileSystemEntries(root).Length == 0)
                    Directory.Delete(root);
            }
            catch (Exception ex)
            {
                TestLog.Detail("RagFixture index cleanup failed: " + ex.Message);
            }
            DocumentIndex.Invalidate();
            Server.Dispose();
            Ws.Dispose();
        }
    }

    internal static class RagTests
    {
        public static void Run()
        {
            VectorStoreTests();
            ScanFilterTests();
            EmbeddingsTests();
            IndexerTests();
            RetrievalTests();
        }

        private static ChunkVector Vector(string file, int start, params float[] embedding)
        {
            return new ChunkVector { File = file, StartLine = start, EndLine = start + 9, Embedding = embedding };
        }

        private static string Join(List<int> values)
        {
            List<string> parts = new List<string>();
            foreach (int value in values)
                parts.Add(value.ToString(CultureInfo.InvariantCulture));
            return string.Join(",", parts.ToArray());
        }

        private static void VectorStoreTests()
        {
            TestRunner.Run("VectorStore.top_k_ranking", () =>
            {
                VectorStore store = new VectorStore();
                store.Add(Vector("opposite.txt", 1, -1f, 0f));
                store.Add(Vector("orthogonal.txt", 1, 0f, 1f));
                store.Add(Vector("diagonal.txt", 1, 1f, 1f));
                store.Add(Vector("near.txt", 1, 1f, 0.1f));
                store.Add(Vector("exact.txt", 1, 2f, 0f));
                store.Add(Vector("zero.txt", 1, 0f, 0f));
                store.Add(Vector("empty.txt", 1));
                store.Add(null);
                TestAssert.Equal(6, store.Count, "empty and null vectors are not stored");

                float[] query = { 1f, 0f };
                List<ChunkHit> top = store.Search(query, 2);
                TestAssert.Equal(2, top.Count, "top 2");
                TestAssert.Equal("exact.txt", top[0].Chunk.File, "best first, whatever its length");
                TestAssert.Equal("near.txt", top[1].Chunk.File, "second");

                List<ChunkHit> all = store.Search(query, 0);
                TestAssert.Equal(5, all.Count, "zero-length vector is never a hit");
                TestAssert.Equal("diagonal.txt", all[2].Chunk.File, "third");
                TestAssert.Equal("opposite.txt", all[4].Chunk.File, "worst last");

                TestAssert.Equal(5, store.Search(query, 50).Count, "top K larger than the store");
                TestAssert.Equal(0, store.Search(new float[] { 0f, 0f }, 3).Count, "zero query");
                TestAssert.Equal(0, store.Search(null, 3).Count, "null query");
                TestAssert.Equal(1, store.Search(new float[] { 1f, 0f, 9f }, 1).Count, "longer query does not throw");
            });

            TestRunner.Run("VectorStore.remove_and_persist", () =>
            {
                using (TempWorkspace ws = new TempWorkspace("cli-vectors"))
                {
                    VectorStore store = new VectorStore();
                    store.Add(Vector(@"C:\k\a.txt", 1, 1f, 0f));
                    store.Add(Vector(@"C:\k\a.txt", 51, 0.5f, 0.5f));
                    store.Add(Vector(@"C:\k\b.txt", 1, 0f, 1f));

                    string path = ws.Combine("sub", "vectors.bin");
                    store.Save(path);
                    VectorStore loaded = VectorStore.Load(path);
                    TestAssert.Equal(3, loaded.Count, "count survives");
                    List<ChunkHit> hits = loaded.Search(new float[] { 0f, 1f }, 1);
                    TestAssert.Equal(@"C:\k\b.txt", hits[0].Chunk.File, "file survives");
                    TestAssert.Equal(1, hits[0].Chunk.StartLine, "start line survives");
                    TestAssert.Equal(10, hits[0].Chunk.EndLine, "end line survives");

                    TestAssert.Equal(2, loaded.RemoveByFile(@"c:\K\A.TXT"), "remove ignores case");
                    TestAssert.Equal(1, loaded.Count, "one left");
                    TestAssert.Equal(0, loaded.RemoveByFile(""), "empty name removes nothing");

                    TestAssert.Equal(0, VectorStore.Load(ws.Combine("missing.bin")).Count, "missing file");

                    string corrupt = ws.Combine("corrupt.bin");
                    File.WriteAllBytes(corrupt, new byte[] { 1, 2, 3 });
                    TestAssert.Equal(0, VectorStore.Load(corrupt).Count, "corrupt file loads empty");

                    byte[] whole = File.ReadAllBytes(path);
                    byte[] half = new byte[whole.Length / 2];
                    Array.Copy(whole, half, half.Length);
                    string truncated = ws.Combine("truncated.bin");
                    File.WriteAllBytes(truncated, half);
                    TestAssert.Equal(0, VectorStore.Load(truncated).Count, "truncated file loads empty, not partial");
                }
            });
        }

        private static void ScanFilterTests()
        {
            TestRunner.Run("DocumentScanFilter.rules", () =>
            {
                TestAssert.True(DocumentScanFilter.LooksBinary(new byte[] { 65, 0, 66 }, 8000), "NUL is binary");
                TestAssert.True(!DocumentScanFilter.LooksBinary(new byte[] { 65, 66 }, 8000), "text");
                TestAssert.True(!DocumentScanFilter.LooksBinary(new byte[] { 65, 66, 67, 0 }, 3), "NUL past the sample");
                TestAssert.True(!DocumentScanFilter.LooksBinary(null, 8000), "null");

                using (TempWorkspace ws = new TempWorkspace("cli-scan"))
                {
                    string ini = ws.Combine("scan.ini");
                    File.WriteAllText(ini, "ragallowedextensions=md, .TXT,,\r\n");
                    HashSet<string> configured = DocumentScanFilter.GetAllowedExtensions(new ConfigHandler(ini));
                    TestAssert.Equal(2, configured.Count, "configured count");
                    TestAssert.True(configured.Contains(".md") && configured.Contains(".txt"), "normalized");

                    HashSet<string> defaults = DocumentScanFilter.GetAllowedExtensions(null);
                    TestAssert.True(defaults.Contains(".md") && defaults.Contains(".txt"), "defaults");
                    TestAssert.True(!defaults.Contains(".exe"), "defaults exclude binaries");

                    string root = ws.Combine("root");
                    Directory.CreateDirectory(Path.Combine(root, "sub", "deeper"));
                    File.WriteAllText(Path.Combine(root, "a.md"), "a");
                    File.WriteAllText(Path.Combine(root, "b.bin"), "b");
                    File.WriteAllText(Path.Combine(root, "noextension"), "c");
                    File.WriteAllText(Path.Combine(root, "sub", "deeper", "c.TXT"), "d");

                    List<string> found = new List<string>(DocumentScanFilter.EnumerateFiles(root, configured));
                    found.Sort(StringComparer.OrdinalIgnoreCase);
                    TestAssert.Equal(2, found.Count, "allowed files only, any depth");
                    TestAssert.Equal(Path.Combine(root, "a.md"), found[0], "full path");
                    TestAssert.Equal(Path.Combine(root, "sub", "deeper", "c.TXT"), found[1], "nested");

                    TestAssert.Equal(0, new List<string>(
                        DocumentScanFilter.EnumerateFiles(ws.Combine("nope"), configured)).Count, "missing folder");
                    TestAssert.Equal(0, new List<string>(
                        DocumentScanFilter.EnumerateFiles(root, new HashSet<string>())).Count, "nothing allowed");
                }
            });
        }

        private static void EmbeddingsTests()
        {
            TestRunner.Run("EmbeddingsClient.batching_and_errors", () =>
            {
                using (RagFixture rag = new RagFixture())
                {
                    EmbeddingsClient client = EmbeddingsClient.CreateFromConfig(rag.Config());
                    TestAssert.True(client != null, "client from config");

                    List<string> texts = new List<string>();
                    for (int i = 0; i < EmbeddingsClient.BatchSize + 8; i++)
                        texts.Add("text " + i);
                    List<float[]> vectors = client.EmbedBatch(texts);
                    TestAssert.Equal(texts.Count, vectors.Count, "one vector per text");
                    TestAssert.Equal(2, rag.Server.EmbeddingsRequestCount, "split into batches");
                    TestAssert.Equal("Bearer k", rag.Server.LastAuthorization, "api key sent");
                    TestAssert.Contains(rag.Server.LastBody, "\"model\":\"test-embed\"", "model sent");

                    TestAssert.Equal(0, client.EmbedBatch(new List<string>()).Count, "nothing to embed");
                    TestAssert.Equal(2, rag.Server.EmbeddingsRequestCount, "no request for nothing");

                    rag.Server.EmbeddingsVectorCount = 1;
                    string shortMessage = EmbedFailure(client);
                    TestAssert.Contains(shortMessage, "fewer vectors than requested", "short response");

                    rag.Server.EmbeddingsVectorCount = 32;
                    rag.Server.EmbeddingsStatusCode = 500;
                    TestAssert.Contains(EmbedFailure(client), "Embeddings request failed", "http failure");

                    TestAssert.True(
                        EmbeddingsClient.CreateFromConfig(new ConfigHandler(rag.Ws.Combine("missing.ini"))) == null,
                        "no endpoint or model means no client");
                }
            });
        }

        private static string EmbedFailure(EmbeddingsClient client)
        {
            try
            {
                client.EmbedBatch(new List<string> { "a", "b", "c" });
            }
            catch (EmbeddingsException ex)
            {
                return ex.Message;
            }
            throw new TestFailureException("expected an EmbeddingsException");
        }

        private static void IndexerTests()
        {
            TestRunner.Run("rag.indexer_first_pass", () =>
            {
                using (RagFixture rag = new RagFixture())
                {
                    rag.WriteFile("notes.txt", RagFixture.Lines("LINE-", 130));
                    rag.WriteFile(Path.Combine("sub", "more.md"), RagFixture.Lines("MORE-", 5));
                    rag.WriteFile("skipped.bin", "not an allowed extension");
                    rag.WriteFile("blank.txt", "  \r\n\r\n");
                    File.WriteAllBytes(Path.Combine(rag.Knowledge, "binary.txt"), new byte[] { 65, 0, 66, 0 });

                    TestAssert.Equal("done", rag.Reconcile(), "final phase");
                    TestAssert.Equal("start", rag.Progress[0].Phase, "first phase");
                    TestAssert.Equal(4, rag.Progress[0].Total, "allowed files counted");
                    TestAssert.Equal(4, rag.Progress[rag.Progress.Count - 2].Current, "progress reaches the total");
                    TestAssert.Equal(1, rag.Server.EmbeddingsRequestCount, "one embeddings batch");

                    DocumentIndex index = rag.OnDisk();
                    TestAssert.True(index.HasIndex, "index saved");
                    TestAssert.Equal("test-embed", index.Manifest.EmbeddingsModel, "model recorded");
                    TestAssert.Equal(60, index.Manifest.IndexChunkLines, "chunk size recorded");
                    TestAssert.True(index.Manifest.Files.ContainsKey(Path.Combine(rag.Knowledge, "notes.txt")), "notes tracked");
                    TestAssert.True(!index.Manifest.Files.ContainsKey(Path.Combine(rag.Knowledge, "binary.txt")), "binary skipped");
                    TestAssert.True(!index.Manifest.Files.ContainsKey(Path.Combine(rag.Knowledge, "skipped.bin")), "extension skipped");

                    // 60-line chunks with a 10-line overlap.
                    TestAssert.Equal("1,51,101", Join(rag.ChunkStarts("notes.txt")), "notes chunk starts");
                    TestAssert.Equal("1", Join(rag.ChunkStarts("more.md")), "small file is one chunk");
                    TestAssert.Equal(0, rag.ChunkStarts("blank.txt").Count, "blank file has no chunks");
                    TestAssert.Equal(4, index.Vectors.Count, "total chunks");
                }
            });

            TestRunner.Run("rag.indexer_incremental", () =>
            {
                using (RagFixture rag = new RagFixture())
                {
                    string notes = rag.WriteFile("notes.txt", RagFixture.Lines("LINE-", 130));
                    string more = rag.WriteFile("more.md", RagFixture.Lines("MORE-", 5));
                    TestAssert.Equal("done", rag.Reconcile(), "first pass");
                    TestAssert.Equal(1, rag.Server.EmbeddingsRequestCount, "first pass embeds");

                    TestAssert.Equal("done", rag.Reconcile(), "unchanged pass");
                    TestAssert.Equal(1, rag.Server.EmbeddingsRequestCount, "unchanged files are not embedded again");
                    TestAssert.Equal(4, rag.OnDisk().Vectors.Count, "unchanged pass keeps the vectors");

                    // Same content, new timestamp: read and compared, not embedded.
                    File.SetLastWriteTimeUtc(notes, DateTime.UtcNow.AddMinutes(5));
                    TestAssert.Equal("done", rag.Reconcile(), "touched pass");
                    TestAssert.Equal(1, rag.Server.EmbeddingsRequestCount, "touched file is not embedded again");

                    File.WriteAllText(more, RagFixture.Lines("CHANGED-", 70));
                    File.SetLastWriteTimeUtc(more, DateTime.UtcNow.AddMinutes(10));
                    TestAssert.Equal("done", rag.Reconcile(), "changed pass");
                    TestAssert.Equal(2, rag.Server.EmbeddingsRequestCount, "changed file embedded");
                    TestAssert.Equal("1,51", Join(rag.ChunkStarts("more.md")), "changed file rechunked");
                    TestAssert.Equal("1,51,101", Join(rag.ChunkStarts("notes.txt")), "other file untouched");

                    File.Delete(more);
                    TestAssert.Equal("done", rag.Reconcile(), "deleted pass");
                    TestAssert.Equal(1, rag.Progress[rag.Progress.Count - 1].FileCount, "file count after delete");
                    TestAssert.Equal(2, rag.Server.EmbeddingsRequestCount, "delete needs no embedding");
                    DocumentIndex index = rag.OnDisk();
                    TestAssert.Equal(3, index.Vectors.Count, "deleted file's chunks removed");
                    TestAssert.True(!index.Manifest.Files.ContainsKey(more), "deleted file untracked");

                    // Changing a chunking setting rebuilds everything.
                    TestAssert.Equal("done", rag.Reconcile("indexchunklines=100\r\n"), "rebuild pass");
                    TestAssert.Equal(3, rag.Server.EmbeddingsRequestCount, "rebuild embeds again");
                    index = rag.OnDisk();
                    TestAssert.Equal(100, index.Manifest.IndexChunkLines, "new chunk size recorded");
                    List<int> starts = new List<int>();
                    foreach (ChunkHit hit in index.Vectors.Search(new float[] { 1f, 0f }, 0))
                        starts.Add(hit.Chunk.StartLine);
                    starts.Sort();
                    TestAssert.Equal("1,91", Join(starts), "rebuilt chunk starts");
                }
            });

            TestRunner.Run("rag.indexer_embeddings_failure_keeps_old_index", () =>
            {
                using (RagFixture rag = new RagFixture())
                {
                    rag.WriteFile("notes.txt", RagFixture.Lines("LINE-", 30));
                    TestAssert.Equal("done", rag.Reconcile(), "first pass");

                    rag.WriteFile("new.txt", RagFixture.Lines("NEW-", 30));
                    rag.Server.EmbeddingsStatusCode = 500;
                    TestAssert.Equal("error", rag.Reconcile(), "failed pass");
                    TestAssert.Contains(rag.LastMessage, "Embeddings failed", "message");
                    TestAssert.Contains(rag.LastMessage, "index not saved", "says nothing was saved");

                    DocumentIndex index = rag.OnDisk();
                    TestAssert.Equal(1, index.Manifest.Files.Count, "saved index untouched");
                    TestAssert.Equal(1, index.Vectors.Count, "saved vectors untouched");

                    // The file that failed is picked up once embeddings work again.
                    rag.Server.EmbeddingsStatusCode = 200;
                    TestAssert.Equal("done", rag.Reconcile(), "retry pass");
                    index = rag.OnDisk();
                    TestAssert.Equal(2, index.Manifest.Files.Count, "new file indexed on retry");
                    TestAssert.Equal(2, index.Vectors.Count, "new file embedded on retry");
                }
            });

            TestRunner.Run("rag.indexer_snippet_cap_covers_every_line", () =>
            {
                using (RagFixture rag = new RagFixture())
                {
                    // 30-character lines cost 36 each against a 100-character cap: two per chunk.
                    StringBuilder content = new StringBuilder();
                    for (int i = 1; i <= 20; i++)
                        content.Append("L" + i.ToString("00", CultureInfo.InvariantCulture) + new string('x', 27) + "\r\n");
                    rag.WriteFile("wide.txt", content.ToString());

                    TestAssert.Equal("done", rag.Reconcile("ragmaxsnippetlength=100\r\n"), "pass");
                    TestAssert.Equal("1,3,5,7,9,11,13,15,17,19", Join(rag.ChunkStarts("wide.txt")), "no line left between chunks");
                }
            });

            TestRunner.Run("rag.indexer_refuses_without_settings", () =>
            {
                using (RagFixture rag = new RagFixture())
                {
                    rag.WriteFile("notes.txt", RagFixture.Lines("LINE-", 5));

                    TestAssert.Equal("error", rag.Reconcile("ragenabled=0\r\n"), "disabled");
                    TestAssert.Contains(rag.LastMessage, "RAG is disabled", "disabled message");

                    TestAssert.Equal("error", rag.Reconcile("embeddingsmodel=\r\n"), "no model");
                    TestAssert.Contains(rag.LastMessage, "Embeddings model is required", "no model message");

                    TestAssert.Equal(0, rag.Server.EmbeddingsRequestCount, "nothing was sent");
                    TestAssert.True(!rag.OnDisk().HasIndex, "nothing was saved");
                }
            });

            TestRunner.Run("rag.indexer_clear", () =>
            {
                using (RagFixture rag = new RagFixture())
                {
                    rag.WriteFile("notes.txt", RagFixture.Lines("LINE-", 5));
                    TestAssert.Equal("done", rag.Reconcile(), "first pass");
                    TestAssert.True(rag.OnDisk().HasIndex, "indexed");

                    rag.Progress.Clear();
                    new DocumentIndexer(rag.Config(), p => rag.Progress.Add(p)).ClearIndex();
                    TestAssert.Equal("cleared", rag.Progress[rag.Progress.Count - 1].Phase, "phase");
                    DocumentIndex index = rag.OnDisk();
                    TestAssert.True(!index.HasIndex, "no vectors");
                    TestAssert.Equal(0, index.Manifest.Files.Count, "no files");
                    TestAssert.True(!DocumentIndex.GetCurrent(rag.Config()).HasIndex, "cached index cleared too");

                    TestAssert.Equal("done", rag.Reconcile(), "reindex after clear");
                    TestAssert.True(rag.OnDisk().HasIndex, "indexed again");
                }
            });
        }

        private static void RetrievalTests()
        {
            TestRunner.Run("rag.retrieval_formats_hits", () =>
            {
                using (RagFixture rag = new RagFixture())
                {
                    string notes = rag.WriteFile("notes.txt", RagFixture.Lines("LINE-", 130));
                    TestAssert.Equal("done", rag.Reconcile(), "indexed");
                    ConfigHandler config = rag.Config();
                    DocumentIndex index = DocumentIndex.GetCurrent(config);
                    TestAssert.True(index.HasIndex, "indexer published the index");

                    IndexedHitSet all = SemanticHitFormatter.Search(index, config, "what is on line one", 5);
                    TestAssert.True(all != null, "hits");
                    TestAssert.Contains(all.FormattedText, "Semantic search results for: what is on line one", "header");
                    TestAssert.Contains(all.FormattedText, notes + ":1-60  (score ", "location line");
                    TestAssert.Contains(all.FormattedText, "\n    LINE-001", "snippet indented");
                    TestAssert.Contains(all.FormattedText, "    LINE-130", "last chunk included");
                    TestAssert.Equal(1, all.FilePaths.Count, "file listed once");
                    TestAssert.Equal("notes.txt", all.FilePaths[0], "file name");

                    IndexedHitSet one = SemanticHitFormatter.Search(index, config, "q", 1);
                    TestAssert.Equal(
                        one.FormattedText.IndexOf("(score ", StringComparison.Ordinal),
                        one.FormattedText.LastIndexOf("(score ", StringComparison.Ordinal),
                        "max results honoured");
                }
            });

            TestRunner.Run("rag.auto_context_merge", () =>
            {
                using (RagFixture rag = new RagFixture())
                {
                    string notes = rag.WriteFile("notes.txt", RagFixture.Lines("LINE-", 10));
                    TestAssert.Equal("done", rag.Reconcile(), "indexed");
                    ConfigHandler config = rag.Config();

                    AutoRagResult plain = AutoRagContext.TryRetrieve(config, "my question", "my question");
                    TestAssert.Equal("[reading notes.txt]", plain.UserStatusLine, "status line");
                    TestAssert.True(
                        plain.MergedPrompt.StartsWith("---Retrieved Context---\n", StringComparison.Ordinal),
                        "context first");
                    TestAssert.True(
                        plain.MergedPrompt.EndsWith("\n---End Retrieved Context---\n\n---\n\nmy question", StringComparison.Ordinal),
                        "question last");
                    TestAssert.Contains(plain.MergedPrompt, "LINE-010", "chunk text");

                    // A prompt that already has a separator gets the context ahead of it.
                    AutoRagResult split = AutoRagContext.TryRetrieve(config, "q", "preamble\n\n---\n\nq");
                    TestAssert.True(
                        split.MergedPrompt.StartsWith("preamble\n\n---Retrieved Context---\n", StringComparison.Ordinal),
                        "context after the preamble");
                    TestAssert.True(split.MergedPrompt.EndsWith("\n\n---\n\nq", StringComparison.Ordinal), "separator kept");

                    AutoRagResult blank = AutoRagContext.TryRetrieve(config, "   ", "   ");
                    TestAssert.Equal("   ", blank.MergedPrompt, "blank query untouched");
                    TestAssert.True(blank.UserStatusLine == null, "blank query has no status");

                    ConfigHandler disabled = rag.Config("ragenabled=0\r\n");
                    TestAssert.Equal("q", AutoRagContext.TryRetrieve(disabled, "q", "q").MergedPrompt, "disabled untouched");

                    // The embeddings server going away must not fail the turn.
                    config = rag.Config();
                    rag.Server.EmbeddingsStatusCode = 500;
                    AutoRagResult down = AutoRagContext.TryRetrieve(config, "q", "q");
                    TestAssert.Equal("q", down.MergedPrompt, "embeddings failure leaves the prompt alone");
                    rag.Server.EmbeddingsStatusCode = 200;

                    // A source file deleted since indexing is skipped rather than quoted.
                    File.Delete(notes);
                    AutoRagResult gone = AutoRagContext.TryRetrieve(config, "q", "q");
                    TestAssert.Equal("q", gone.MergedPrompt, "deleted source untouched");
                    TestAssert.True(gone.UserStatusLine == null, "deleted source has no status");
                }
            });
        }
    }
}
