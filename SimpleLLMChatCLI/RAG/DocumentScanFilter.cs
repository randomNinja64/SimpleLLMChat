using System;
using System.Collections.Generic;
using System.IO;

namespace SimpleLLMChatCLI.RAG
{
    /// <summary>
    /// Extension whitelist for knowledge-folder indexing.
    /// </summary>
    public static class DocumentScanFilter
    {
        /// <summary>Byte sample size for the text/binary heuristic.</summary>
        public const int BinarySampleBytes = 8000;

        public static HashSet<string> GetAllowedExtensions(ConfigHandler config)
        {
            List<string> fromConfig = config != null ? config.GetConfigList("ragAllowedExtensions") : null;
            IEnumerable<string> source = (fromConfig != null && fromConfig.Count > 0)
                ? (IEnumerable<string>)fromConfig
                : AppConstants.DefaultRagAllowedExtensions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string token in source)
            {
                string ext = RagExtensionList.NormalizeExtension(token);
                if (!string.IsNullOrEmpty(ext))
                    set.Add(ext);
            }
            return set;
        }

        public static bool IsAllowedExtension(string filePath, HashSet<string> allowed)
        {
            if (string.IsNullOrEmpty(filePath) || allowed == null || allowed.Count == 0)
                return false;
            string ext = Path.GetExtension(filePath);
            return !string.IsNullOrEmpty(ext) && allowed.Contains(ext);
        }

        /// <summary>
        /// Heuristic: a file is treated as binary if a NUL byte appears within the sampled
        /// prefix (mislabeled/corrupted files with an allowed extension are skipped rather
        /// than lenient-decoded as garbage text).
        /// </summary>
        public static bool LooksBinary(byte[] sample, int sampleLength)
        {
            if (sample == null)
                return false;
            int limit = Math.Min(sampleLength, sample.Length);
            for (int i = 0; i < limit; i++)
            {
                if (sample[i] == 0)
                    return true;
            }
            return false;
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        public static IEnumerable<string> EnumerateFiles(string rootPath, HashSet<string> allowed)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                yield break;

            var stack = new Stack<string>();
            stack.Push(rootPath);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string[] files;
                string[] subdirs;
                try { files = Directory.GetFiles(dir); }
                catch { continue; }
                try { subdirs = Directory.GetDirectories(dir); }
                catch { subdirs = new string[0]; }

                foreach (string file in files)
                {
                    if (IsAllowedExtension(file, allowed))
                        yield return NormalizePath(file);
                }

                foreach (string sub in subdirs)
                    stack.Push(sub);
            }
        }
    }
}
