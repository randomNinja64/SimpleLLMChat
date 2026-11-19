using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileTools
{
    internal static class EditFileTool
    {
        private struct Block
        {
            public string Search;
            public string Replace;
        }

        internal static string Apply(string filePath, JArray edits, out int exitCode)
        {
            exitCode = 0;

            try
            {
                string expandedPath = Environment.ExpandEnvironmentVariables(filePath ?? string.Empty);
                if (string.IsNullOrEmpty(expandedPath))
                {
                    exitCode = 1;
                    return "error: File path cannot be empty";
                }

                if (!File.Exists(expandedPath))
                {
                    exitCode = 1;
                    return "error: File does not exist: " + expandedPath;
                }

                List<Block> blocks = ParseEdits(edits);
                if (blocks.Count == 0)
                {
                    exitCode = 1;
                    return "error: No edits provided. Provide at least one edit with old_string and new_string.";
                }

                // Edits are spliced into the text as read, so everything outside them keeps
                // its bytes; the encoding is remembered so the file is written back the same way.
                Encoding encoding;
                string original = ReadDetectingEncoding(expandedPath, out encoding);

                List<string> errors;
                string newContent;
                int appliedCount = ApplyBlocksInMemory(original, blocks, out newContent, out errors);

                if (errors.Count > 0)
                {
                    exitCode = 1;
                    StringBuilder errSb = new StringBuilder();
                    errSb.AppendLine("error: edit failed:");
                    foreach (string err in errors) errSb.AppendLine(err);
                    return errSb.ToString();
                }

                if (string.Equals(newContent, original, StringComparison.Ordinal))
                {
                    return "No changes were necessary (file already matches).";
                }

                File.WriteAllText(expandedPath, newContent, encoding);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Applied " + appliedCount + " edit(s).");
                string diff = BuildUnifiedDiff(original, newContent, 200);
                if (!string.IsNullOrEmpty(diff))
                {
                    sb.AppendLine();
                    sb.Append(diff);
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                exitCode = 1;
                return "error: " + ex.Message;
            }
        }

        /// <summary>
        /// Reads a text file and reports the encoding to write it back with: a BOM decides
        /// when present; otherwise valid UTF-8 stays BOM-less UTF-8 and anything else is
        /// treated as the system ANSI code page.
        /// </summary>
        private static string ReadDetectingEncoding(string path, out Encoding encoding)
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                encoding = new UTF8Encoding(true);
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                encoding = Encoding.Unicode;
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                encoding = Encoding.BigEndianUnicode;
            else
            {
                try
                {
                    string text = new UTF8Encoding(false, true).GetString(bytes);
                    encoding = new UTF8Encoding(false);
                    return text;
                }
                catch (DecoderFallbackException)
                {
                    encoding = Encoding.Default;
                }
            }

            int preamble = encoding.GetPreamble().Length;
            return encoding.GetString(bytes, preamble, bytes.Length - preamble);
        }

        private static int ApplyBlocksInMemory(string originalContent, List<Block> blocks,
            out string newContent, out List<string> errors)
        {
            errors = new List<string>();
            string current = originalContent ?? string.Empty;
            int appliedCount = 0;

            for (int i = 0; i < blocks.Count; i++)
            {
                Block block = blocks[i];
                int blockNum = i + 1;

                // Match on an LF-normalized view, but splice into the text as read so lines
                // outside the edit keep their own line endings.
                string normalized = TextNormalization.NormalizeLineEndings(current);
                int occurrences = CountOccurrences(normalized, block.Search);
                if (occurrences == 0)
                {
                    errors.Add(BuildNotFoundError(block.Search, blockNum));
                    continue;
                }

                if (occurrences != 1)
                {
                    errors.Add(
                        "Edit " + blockNum + " failed: old_string appears " + occurrences + " times.\n" +
                        "old_string must match EXACTLY once. Add more surrounding context to make it unique.");
                    continue;
                }

                int index = normalized.IndexOf(block.Search, StringComparison.Ordinal);
                int start = ToOriginalOffset(current, index);
                int end = ToOriginalOffset(current, index + block.Search.Length);
                // New lines take CRLF when the file uses it.
                string replacement = current.Contains("\r\n") ? block.Replace.Replace("\n", "\r\n") : block.Replace;
                current = current.Substring(0, start) + replacement + current.Substring(end);
                appliedCount++;
            }

            newContent = current;
            return appliedCount;
        }

        /// <summary>
        /// Maps an offset in the LF-normalized text back to the text as read, where a CRLF
        /// is two characters instead of one.
        /// </summary>
        private static int ToOriginalOffset(string text, int normalizedOffset)
        {
            int i = 0;
            for (int n = 0; n < normalizedOffset; n++)
                i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            return i;
        }

        private static List<Block> ParseEdits(JArray edits)
        {
            List<Block> blocks = new List<Block>();
            if (edits == null) return blocks;

            foreach (JToken token in edits)
            {
                JObject obj = token as JObject;
                if (obj == null) continue;

                string search = (string)obj["old_string"] ?? string.Empty;
                string replace = (string)obj["new_string"] ?? string.Empty;

                blocks.Add(new Block
                {
                    Search = TextNormalization.NormalizeLineEndings(search),
                    Replace = TextNormalization.NormalizeLineEndings(replace)
                });
            }

            return blocks;
        }

        private static int CountOccurrences(string text, string search)
        {
            if (string.IsNullOrEmpty(search)) return 0;

            int count = 0;
            int index = 0;
            while (true)
            {
                index = text.IndexOf(search, index, StringComparison.Ordinal);
                if (index < 0) break;
                count++;
                index += search.Length;
            }
            return count;
        }

        private static string BuildNotFoundError(string searchText, int blockNum)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Edit " + blockNum + " failed: old_string not found.");
            sb.AppendLine("old_string was:");
            sb.AppendLine(searchText);
            sb.AppendLine();
            sb.AppendLine("old_string must match EXACTLY, including whitespace, indentation, and line endings.");
            return sb.ToString();
        }

        private static string BuildUnifiedDiff(string oldText, string newText, int maxLines)
        {
            string[] a = TextNormalization.NormalizeLineEndings(oldText ?? string.Empty).Split('\n');
            string[] b = TextNormalization.NormalizeLineEndings(newText ?? string.Empty).Split('\n');

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("--- original");
            sb.AppendLine("+++ new");

            int i = 0;
            int j = 0;
            int linesOut = 0;

            while (i < a.Length || j < b.Length)
            {
                if (linesOut >= maxLines)
                {
                    sb.AppendLine("...(diff truncated)");
                    break;
                }

                string la = i < a.Length ? a[i] : null;
                string lb = j < b.Length ? b[j] : null;

                if (la == lb)
                {
                    if (la != null)
                    {
                        sb.AppendLine(" " + la);
                        linesOut++;
                    }
                    i++;
                    j++;
                    continue;
                }

                if (la != null)
                {
                    sb.AppendLine("-" + la);
                    linesOut++;
                    i++;
                }

                if (lb != null)
                {
                    sb.AppendLine("+" + lb);
                    linesOut++;
                    j++;
                }
            }

            return sb.ToString();
        }
    }
}
