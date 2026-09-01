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
                    return "File path cannot be empty";
                }

                if (!File.Exists(expandedPath))
                {
                    exitCode = 1;
                    return "File does not exist: " + expandedPath;
                }

                List<Block> blocks = ParseEdits(edits);
                if (blocks.Count == 0)
                {
                    exitCode = 1;
                    return "No edits provided. Provide at least one edit with old_string and new_string.";
                }

                string original = TextNormalization.NormalizeLineEndings(
                    File.ReadAllText(expandedPath, Encoding.UTF8));

                List<string> errors;
                string newContent;
                int appliedCount = ApplyBlocksInMemory(original, blocks, out newContent, out errors);

                if (errors.Count > 0)
                {
                    exitCode = 1;
                    StringBuilder errSb = new StringBuilder();
                    errSb.AppendLine("Errors:");
                    foreach (string err in errors) errSb.AppendLine(err);
                    return errSb.ToString();
                }

                if (string.Equals(newContent, original, StringComparison.Ordinal))
                {
                    return "No changes were necessary (file already matches).";
                }

                File.WriteAllText(expandedPath, newContent, Encoding.UTF8);

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
                return "Error: " + ex.Message;
            }
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

                int occurrences = CountOccurrences(current, block.Search);
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

                int index = current.IndexOf(block.Search, StringComparison.Ordinal);
                current = current.Substring(0, index) + block.Replace + current.Substring(index + block.Search.Length);
                appliedCount++;
            }

            newContent = current;
            return appliedCount;
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
