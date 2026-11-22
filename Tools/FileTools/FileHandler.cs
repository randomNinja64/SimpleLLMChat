using System;
using System.IO;
using System.Text;

namespace FileTools
{
    /// <summary>
    /// File operations. Each returns the text for the model and throws on failure;
    /// ToolHelper.RunToolMain turns the exception into an "error: ..." result.
    /// </summary>
    public static class FileHandler
    {
        /// <summary>
        /// Reads up to maxLines lines starting at a 0-indexed line offset. maxContentLength
        /// (characters, 0 = no limit) also caps the page: the line that crosses it is cut there.
        /// </summary>
        public static string ReadFile(string filename, int maxLines, int maxContentLength, int lineOffset = 0)
        {
            filename = Environment.ExpandEnvironmentVariables(filename);
            if (!File.Exists(filename))
                throw new FileNotFoundException($"File not found: {filename}");

            // The whole file is split so the reported total is the real line count.
            string content = TextNormalization.NormalizeLineEndings(
                TextNormalization.ReadDetectingEncoding(filename, out _));
            if (content.Length == 0)
                return "File is empty (0 lines).";

            string[] lines = content.Split('\n');
            int totalLines = lines.Length;
            if (content.EndsWith("\n"))
                totalLines--; // a trailing newline ends the last line rather than starting a new one

            if (maxLines < 1)
                maxLines = 1;
            if (lineOffset < 0)
                lineOffset = 0;
            if (lineOffset >= totalLines)
                throw new ArgumentException($"File has {totalLines} lines (0-indexed). Line offset {lineOffset} exceeds file length.");

            StringBuilder body = new StringBuilder();
            int end = lineOffset; // exclusive
            string truncatedBy = null;
            string charLimit = $"character limit ({maxContentLength})";
            while (end < totalLines)
            {
                if (end - lineOffset >= maxLines)
                {
                    truncatedBy = $"line limit ({maxLines})";
                    break;
                }

                string line = lines[end];
                if (body.Length > 0)
                {
                    if (maxContentLength > 0 && body.Length >= maxContentLength)
                    {
                        truncatedBy = charLimit;
                        break;
                    }
                    body.Append('\n');
                }
                end++;

                // A line that does not fit is cut at the character limit, which ends the page.
                int room = maxContentLength > 0 ? maxContentLength - body.Length : int.MaxValue;
                if (line.Length > room)
                {
                    body.Append(line, 0, Math.Max(room, 0)).Append("...[line truncated]");
                    truncatedBy = charLimit;
                    break;
                }
                body.Append(line);
            }

            StringBuilder result = new StringBuilder();
            result.AppendLine($"File has {totalLines} lines, reading lines {lineOffset}-{end - 1}");
            result.AppendLine("---");
            result.Append(body.ToString());

            if (truncatedBy != null)
                result.AppendLine($"\n...[truncated: {truncatedBy}]");

            return result.ToString();
        }

        public static string WriteFile(string filename, string content)
        {
            filename = Environment.ExpandEnvironmentVariables(filename);
            EnsureDirectoryExists(Path.GetDirectoryName(filename));

            // UTF-8 without a BOM (a BOM breaks the first line of .bat/.cmd files), unless
            // the file being replaced already has one.
            File.WriteAllText(filename, content, TextNormalization.EncodingForOverwrite(filename));
            return $"File written successfully: {filename}";
        }

        public static string MoveFile(string sourcePath, string destinationPath)
        {
            PrepareFileOperation(ref sourcePath, ref destinationPath);
            File.Move(sourcePath, destinationPath);
            return $"File moved successfully from '{sourcePath}' to '{destinationPath}'";
        }

        public static string CopyFile(string sourcePath, string destinationPath)
        {
            PrepareFileOperation(ref sourcePath, ref destinationPath);
            File.Copy(sourcePath, destinationPath);
            return $"File copied successfully from '{sourcePath}' to '{destinationPath}'";
        }

        // Shared checks for move and copy: the source must exist, the destination must not.
        private static void PrepareFileOperation(ref string sourcePath, ref string destinationPath)
        {
            sourcePath = Environment.ExpandEnvironmentVariables(sourcePath);
            destinationPath = Environment.ExpandEnvironmentVariables(destinationPath);

            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Source file not found: {sourcePath}");
            if (File.Exists(destinationPath))
                throw new IOException($"Destination file already exists: {destinationPath}");

            EnsureDirectoryExists(Path.GetDirectoryName(destinationPath));
        }

        public static string DeleteFile(string filePath)
        {
            filePath = Environment.ExpandEnvironmentVariables(filePath);
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            File.Delete(filePath);
            return $"File deleted successfully: {filePath}";
        }

        public static string ListDirectory(string directoryPath)
        {
            directoryPath = Environment.ExpandEnvironmentVariables(directoryPath);
            if (!Directory.Exists(directoryPath))
                throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

            StringBuilder result = new StringBuilder();
            result.AppendLine($"Contents of: {directoryPath}");
            result.AppendLine();

            string[] directories = Directory.GetDirectories(directoryPath);
            if (directories.Length > 0)
            {
                result.AppendLine("Directories:");
                foreach (string dir in directories)
                    result.AppendLine($"  [DIR]  {Path.GetFileName(dir)}");
                result.AppendLine();
            }

            string[] files = Directory.GetFiles(directoryPath);
            if (files.Length > 0)
            {
                result.AppendLine("Files:");
                foreach (string file in files)
                    result.AppendLine($"  [FILE] {Path.GetFileName(file)} ({FormatFileSize(new FileInfo(file).Length)})");
            }

            if (directories.Length == 0 && files.Length == 0)
                result.AppendLine("Directory is empty.");

            return result.ToString();
        }

        /// <summary>Extracts with 7za.exe; a failed extraction keeps 7za's exit code.</summary>
        public static ToolResult ExtractFile(string archivePath, string destinationPath)
        {
            archivePath = Environment.ExpandEnvironmentVariables(archivePath);
            destinationPath = Environment.ExpandEnvironmentVariables(destinationPath);

            if (!File.Exists(archivePath))
                throw new FileNotFoundException($"Archive not found: {archivePath}");

            EnsureDirectoryExists(destinationPath);

            // x (extract with full paths) -o (output directory) -y (yes to all prompts)
            string arguments = $"x \"{archivePath}\" -o\"{destinationPath}\" -y";
            string output = ToolHelper.ExecuteProcess("7za.exe", arguments, out int exitCode);

            if (exitCode != 0)
                return new ToolResult($"error: 7za exited with code {exitCode}:\n{output}", exitCode);

            return new ToolResult($"Archive extracted successfully to: {destinationPath}\n{output}", 0);
        }

        private static void EnsureDirectoryExists(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath) || Directory.Exists(directoryPath))
                return;

            try
            {
                Directory.CreateDirectory(directoryPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to create directory '{directoryPath}': {ex.Message}", ex);
            }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return string.Format("{0:0.##} {1}", len, sizes[order]);
        }
    }
}
