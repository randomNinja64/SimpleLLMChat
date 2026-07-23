using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SimpleLLMChatCLI.RAG
{
    public class ChunkVector
    {
        public string File;
        public int StartLine;
        public int EndLine;
        public float[] Embedding;
        public double Norm;

        public void ComputeNorm()
        {
            double sum = 0;
            if (Embedding != null)
            {
                for (int i = 0; i < Embedding.Length; i++)
                    sum += (double)Embedding[i] * Embedding[i];
            }
            Norm = Math.Sqrt(sum);
        }
    }

    public class ChunkHit
    {
        public ChunkVector Chunk;
        public double Score;
    }

    /// <summary>
    /// In-memory chunk embeddings with binary persistence and brute-force cosine search
    /// that retains only the top-K hits while scanning (no full-result sort).
    /// Snippet text is not stored; callers re-read from disk at retrieve time (NyoCoder-style).
    /// </summary>
    public class VectorStore
    {
        private readonly List<ChunkVector> _vectors = new List<ChunkVector>();
        private int _dimension;

        public int Count { get { return _vectors.Count; } }

        public void Add(ChunkVector vector)
        {
            if (vector == null || vector.Embedding == null || vector.Embedding.Length == 0)
                return;
            if (_dimension == 0)
                _dimension = vector.Embedding.Length;
            vector.ComputeNorm();
            _vectors.Add(vector);
        }

        public void Clear()
        {
            _vectors.Clear();
            _dimension = 0;
        }

        public int RemoveByFile(string file)
        {
            if (string.IsNullOrEmpty(file))
                return 0;
            int removed = _vectors.RemoveAll(v =>
                string.Equals(v.File, file, StringComparison.OrdinalIgnoreCase));
            if (_vectors.Count == 0)
                _dimension = 0;
            return removed;
        }

        /// <summary>
        /// Returns the top-K chunks by cosine similarity to <paramref name="query"/>.
        /// When <paramref name="topK"/> is &lt;= 0, all scored chunks are returned (sorted).
        /// </summary>
        public List<ChunkHit> Search(float[] query, int topK)
        {
            List<ChunkHit> hits = new List<ChunkHit>();
            if (query == null || query.Length == 0 || _vectors.Count == 0)
                return hits;

            double queryNorm = 0;
            for (int i = 0; i < query.Length; i++)
                queryNorm += (double)query[i] * query[i];
            queryNorm = Math.Sqrt(queryNorm);
            if (queryNorm == 0)
                return hits;

            // Bounded top-K: keep at most topK candidates while scanning (min-of-K eviction).
            bool limit = topK > 0;
            int worstIndex = -1;
            double worstScore = 0;

            foreach (ChunkVector v in _vectors)
            {
                if (v.Norm == 0)
                    continue;
                double dot = Dot(query, v.Embedding);
                double score = dot / (queryNorm * v.Norm);

                if (!limit)
                {
                    hits.Add(new ChunkHit { Chunk = v, Score = score });
                    continue;
                }

                if (hits.Count < topK)
                {
                    hits.Add(new ChunkHit { Chunk = v, Score = score });
                    if (worstIndex < 0 || score < worstScore)
                    {
                        worstIndex = hits.Count - 1;
                        worstScore = score;
                    }
                    continue;
                }

                if (score <= worstScore)
                    continue;

                hits[worstIndex] = new ChunkHit { Chunk = v, Score = score };
                worstIndex = IndexOfLowestScore(hits);
                worstScore = hits[worstIndex].Score;
            }

            hits.Sort((a, b) => b.Score.CompareTo(a.Score));
            return hits;
        }

        private static int IndexOfLowestScore(List<ChunkHit> hits)
        {
            int worst = 0;
            for (int i = 1; i < hits.Count; i++)
            {
                if (hits[i].Score < hits[worst].Score)
                    worst = i;
            }
            return worst;
        }

        private static double Dot(float[] a, float[] b)
        {
            int len = Math.Min(a.Length, b.Length);
            double sum = 0;
            for (int i = 0; i < len; i++)
                sum += (double)a[i] * b[i];
            return sum;
        }

        public void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(fs, Encoding.UTF8))
            {
                writer.Write(_dimension);
                writer.Write(_vectors.Count);

                foreach (ChunkVector v in _vectors)
                {
                    writer.Write(v.File ?? string.Empty);
                    writer.Write(v.StartLine);
                    writer.Write(v.EndLine);
                    int len = v.Embedding != null ? v.Embedding.Length : 0;
                    writer.Write(len);
                    for (int i = 0; i < len; i++)
                        writer.Write(v.Embedding[i]);
                }
            }
        }

        public static VectorStore Load(string path)
        {
            VectorStore store = new VectorStore();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return store;

            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fs, Encoding.UTF8))
                {
                    int dimension = reader.ReadInt32();
                    int count = reader.ReadInt32();
                    store._dimension = dimension;

                    for (int c = 0; c < count; c++)
                    {
                        ChunkVector v = new ChunkVector();
                        v.File = reader.ReadString();
                        v.StartLine = reader.ReadInt32();
                        v.EndLine = reader.ReadInt32();
                        int len = reader.ReadInt32();
                        float[] embedding = new float[len];
                        for (int i = 0; i < len; i++)
                            embedding[i] = reader.ReadSingle();
                        v.Embedding = embedding;
                        v.ComputeNorm();
                        store._vectors.Add(v);
                    }
                }
            }
            catch
            {
                return new VectorStore();
            }

            return store;
        }
    }
}
