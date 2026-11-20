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

        /// <summary>Applies the edits and returns a summary with a diff. Throws when an edit fails.</summary>
        internal static string Apply(string filePath, JArray edits)
        {
            string expandedPath = Environment.ExpandEnvironmentVariables(filePath ?? string.Empty);
            if (string.IsNullOrEmpty(expandedPath))
                throw new ArgumentException("File path cannot be empty");
            if (!File.Exists(expandedPath))
                throw new FileNotFoundException("File does not exist: " + expandedPath);

            List<Block> blocks = ParseEdits(edits);
            if (blocks.Count == 0)
                throw new ArgumentException("No edits provided. Provide at least one edit with old_string and new_string.");

            // Edits are spliced into the text as read, so everything outside them keeps
            // its bytes; the encoding is remembered so the file is written back the same way.
            Encoding encoding;
            string original = TextNormalization.ReadDetectingEncoding(expandedPath, out encoding);

            List<string> errors;
            string newContent;
            int appliedCount = ApplyBlocksInMemory(original, blocks, out newContent, out errors);

            if (errors.Count > 0)
                throw new InvalidOperationException("edit failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors.ToArray()));

            if (string.Equals(newContent, original, StringComparison.Ordinal))
                return "No changes were necessary (file already matches).";

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

        private enum DiffOpType { Equal, Delete, Insert }

        private struct DiffOp
        {
            public DiffOpType Type;
            public string Text;
        }

        /// <summary>
        /// LCS-based line diff producing Equal / Delete / Insert operations.
        /// Deletions are ordered before insertions within each hunk.
        /// </summary>
        private static List<DiffOp> ComputeLineDiff(string[] oldLines, string[] newLines)
        {
            int m = oldLines.Length;
            int n = newLines.Length;

            // Build LCS table
            int[,] dp = new int[m + 1, n + 1];
            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (string.Equals(oldLines[i - 1], newLines[j - 1], StringComparison.Ordinal))
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }

            // Backtrack - prefer consuming from new first (Insert) when tied,
            // so that after reversal deletes precede inserts within each hunk.
            List<DiffOp> ops = new List<DiffOp>();
            int ii = m, jj = n;
            while (ii > 0 || jj > 0)
            {
                if (ii > 0 && jj > 0 && string.Equals(oldLines[ii - 1], newLines[jj - 1], StringComparison.Ordinal))
                {
                    ops.Add(new DiffOp { Type = DiffOpType.Equal, Text = oldLines[ii - 1] });
                    ii--; jj--;
                }
                else if (jj > 0 && (ii == 0 || dp[ii, jj - 1] >= dp[ii - 1, jj]))
                {
                    ops.Add(new DiffOp { Type = DiffOpType.Insert, Text = newLines[jj - 1] });
                    jj--;
                }
                else
                {
                    ops.Add(new DiffOp { Type = DiffOpType.Delete, Text = oldLines[ii - 1] });
                    ii--;
                }
            }

            ops.Reverse();
            return ops;
        }

        private static string BuildUnifiedDiff(string oldText, string newText, int maxLines)
        {
            // Changed lines with a few unchanged lines around each group, like a unified diff.
            const int Context = 3;
            const int MaxDiffLines = 1000;

            string[] a = string.IsNullOrEmpty(oldText) ? new string[0] : TextNormalization.NormalizeLineEndings(oldText).Split('\n');
            string[] b = string.IsNullOrEmpty(newText) ? new string[0] : TextNormalization.NormalizeLineEndings(newText).Split('\n');

            // Skip the unchanged start and end so the line diff only runs over the changed middle.
            int prefix = 0;
            while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
                prefix++;
            int suffix = 0;
            while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
                suffix++;

            string[] midA = new string[a.Length - prefix - suffix];
            string[] midB = new string[b.Length - prefix - suffix];
            Array.Copy(a, prefix, midA, 0, midA.Length);
            Array.Copy(b, prefix, midB, 0, midB.Length);

            int lead = Math.Min(prefix, Context);
            int trail = Math.Min(suffix, Context);
            List<DiffOp> ops = new List<DiffOp>();
            for (int k = prefix - lead; k < prefix; k++)
                ops.Add(new DiffOp { Type = DiffOpType.Equal, Text = a[k] });
            if (midA.Length > MaxDiffLines || midB.Length > MaxDiffLines)
            {
                // Too large to line-diff: show the old block followed by the new one.
                foreach (string line in midA) ops.Add(new DiffOp { Type = DiffOpType.Delete, Text = line });
                foreach (string line in midB) ops.Add(new DiffOp { Type = DiffOpType.Insert, Text = line });
            }
            else
            {
                ops.AddRange(ComputeLineDiff(midA, midB));
            }
            for (int k = a.Length - suffix; k < a.Length - suffix + trail; k++)
                ops.Add(new DiffOp { Type = DiffOpType.Equal, Text = a[k] });

            bool[] show = new bool[ops.Count];
            for (int i = 0; i < ops.Count; i++)
            {
                if (ops[i].Type == DiffOpType.Equal)
                    continue;
                for (int k = Math.Max(0, i - Context); k <= Math.Min(ops.Count - 1, i + Context); k++)
                    show[k] = true;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("--- original");
            sb.AppendLine("+++ new");

            int oldLine = prefix - lead + 1;
            int newLine = oldLine;
            int linesOut = 0;
            bool inHunk = false;
            for (int i = 0; i < ops.Count; i++)
            {
                DiffOp op = ops[i];
                if (show[i])
                {
                    if (linesOut >= maxLines)
                    {
                        sb.AppendLine("...(diff truncated)");
                        break;
                    }
                    if (!inHunk)
                        sb.AppendLine("@@ -" + oldLine + " +" + newLine + " @@");
                    inHunk = true;

                    string marker = op.Type == DiffOpType.Equal ? " " : op.Type == DiffOpType.Delete ? "-" : "+";
                    sb.AppendLine(marker + op.Text);
                    linesOut++;
                }
                else
                {
                    inHunk = false;
                }

                if (op.Type != DiffOpType.Insert) oldLine++;
                if (op.Type != DiffOpType.Delete) newLine++;
            }

            return sb.ToString();
        }
    }
}
