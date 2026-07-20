using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SimpleLLMChatCLI.RAG
{
    public class IndexFileEntry
    {
        public string Hash;
    }

    public class IndexManifest
    {
        public string KnowledgePath;
        public string EmbeddingsModel;
        public int IndexChunkLines;
        public int IndexChunkOverlap;
        public int RagMaxSnippetLength;
        public Dictionary<string, IndexFileEntry> Files =
            new Dictionary<string, IndexFileEntry>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// On-disk document index: manifest.json + vectors.bin under rag-index/{md5(path)}/.
    /// Holds a process-wide cache so retrieval does not re-read vectors.bin every turn.
    /// </summary>
    public class DocumentIndex
    {
        private static readonly object CurrentGate = new object();
        private static DocumentIndex _current;

        public string KnowledgePath { get; private set; }
        public string IndexDir { get; private set; }
        public IndexManifest Manifest { get; private set; }
        public VectorStore Vectors { get; private set; }

        public bool HasIndex
        {
            get { return Vectors != null && Vectors.Count > 0; }
        }

        public string ManifestPath
        {
            get { return Path.Combine(IndexDir, "manifest.json"); }
        }

        public string VectorsPath
        {
            get { return Path.Combine(IndexDir, "vectors.bin"); }
        }

        /// <summary>
        /// Returns the cached index for the configured knowledge path, (re)loading from disk
        /// if the cache is empty or the knowledge path changed.
        /// </summary>
        public static DocumentIndex GetCurrent(ConfigHandler config)
        {
            string knowledgePath = ResolveKnowledgePath(config);
            lock (CurrentGate)
            {
                if (_current != null
                    && string.Equals(_current.KnowledgePath, knowledgePath, StringComparison.OrdinalIgnoreCase))
                    return _current;

                _current = LoadFor(knowledgePath);
                return _current;
            }
        }

        /// <summary>Replaces the cached index (called by the indexer after a run).</summary>
        public static void SetCurrent(DocumentIndex index)
        {
            lock (CurrentGate) { _current = index; }
        }

        /// <summary>Drops the cached index so the next access reloads from disk.</summary>
        public static void Invalidate()
        {
            lock (CurrentGate) { _current = null; }
        }

        public static string GetIndexRootDir()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rag-index");
        }

        public static string HashMd5Hex(string value, bool lowercase)
        {
            string input = value ?? string.Empty;
            if (lowercase)
                input = input.ToLowerInvariant();
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static string ResolveKnowledgePath(ConfigHandler config)
        {
            string path = config != null ? config.GetConfigString("ragKnowledgePath") : null;
            if (string.IsNullOrEmpty(path))
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "knowledge");
            try
            {
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
            }
            catch
            {
                return path.Trim();
            }
        }

        public static DocumentIndex LoadFor(string knowledgePath)
        {
            DocumentIndex index = new DocumentIndex();
            index.KnowledgePath = knowledgePath;
            index.IndexDir = Path.Combine(GetIndexRootDir(), HashMd5Hex(knowledgePath, true));
            index.Manifest = LoadManifest(index.ManifestPath) ?? new IndexManifest();
            if (index.Manifest.Files == null)
                index.Manifest.Files = new Dictionary<string, IndexFileEntry>(StringComparer.OrdinalIgnoreCase);
            index.Vectors = VectorStore.Load(index.VectorsPath);
            return index;
        }

        public void Save()
        {
            if (!Directory.Exists(IndexDir))
                Directory.CreateDirectory(IndexDir);

            Manifest.KnowledgePath = KnowledgePath;

            string json = JsonConvert.SerializeObject(Manifest, Formatting.Indented);
            File.WriteAllText(ManifestPath, json, Encoding.UTF8);
            Vectors.Save(VectorsPath);
        }

        public void Clear()
        {
            Vectors.Clear();
            Manifest.Files.Clear();
            try
            {
                if (File.Exists(VectorsPath))
                    File.Delete(VectorsPath);
                if (File.Exists(ManifestPath))
                    File.Delete(ManifestPath);
            }
            catch { }
        }

        public void RemoveFileData(string filePath)
        {
            string key = DocumentScanFilter.NormalizePath(filePath);
            Vectors.RemoveByFile(key);
            Manifest.Files.Remove(key);
        }

        public string ToDisplayPath(string absolutePath)
        {
            try
            {
                string root = KnowledgePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return absolutePath.Substring(root.Length);
            }
            catch { }
            return absolutePath;
        }

        /// <summary>True when stored index params differ from current config (requires full rebuild).</summary>
        public bool ParamsMismatch(ConfigHandler config)
        {
            if (config == null)
                return true;
            string model = config.GetConfigString("embeddingsModel") ?? string.Empty;
            if (!string.Equals(Manifest.EmbeddingsModel ?? string.Empty, model, StringComparison.Ordinal))
                return true;
            if (Manifest.IndexChunkLines != config.GetConfigInt("indexChunkLines", 60))
                return true;
            if (Manifest.IndexChunkOverlap != config.GetConfigInt("indexChunkOverlap", 10))
                return true;
            if (Manifest.RagMaxSnippetLength != config.GetConfigInt("ragMaxSnippetLength", 2000))
                return true;
            return false;
        }

        private static IndexManifest LoadManifest(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                IndexManifest manifest = JsonConvert.DeserializeObject<IndexManifest>(json);
                if (manifest != null && manifest.Files == null)
                    manifest.Files = new Dictionary<string, IndexFileEntry>(StringComparer.OrdinalIgnoreCase);
                return manifest;
            }
            catch
            {
                return null;
            }
        }
    }
}
