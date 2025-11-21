using System;
using System.IO;
using System.Text;

public static class FileHandler
{
    public static string ReadFile(string filename, out int exitCode, int offset = 0)
    {
        exitCode = 0;

        try
        {
            // Expand environment variables in filename
            filename = Environment.ExpandEnvironmentVariables(filename);

            if (!File.Exists(filename))
            {
                exitCode = 1;
                return $"File not found: {filename}";
            }

            string content = File.ReadAllText(filename, Encoding.UTF8);
            int totalLength = content.Length;

            // Validate offset
            if (offset < 0)
                offset = 0;
            if (offset >= totalLength)
            {
                exitCode = 1;
                return $"File length = {totalLength} characters. Offset {offset} exceeds file length.";
            }

            // Always read up to MAX_CONTENT_LENGTH characters (or until EOF)
            int endPos = Math.Min(offset + SimpleLLMChatCLI.Program.MAX_CONTENT_LENGTH, totalLength);
            string excerpt = content.Substring(offset, endPos - offset);

            // Build result with header
            StringBuilder result = new StringBuilder();
            result.AppendLine($"File length = {totalLength} characters, reading chars {offset}-{endPos - 1}");
            result.AppendLine("---");
            result.Append(excerpt);

            if (endPos < totalLength)
            {
                result.AppendLine("\n...[truncated]");
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error reading file: " + ex.Message;
        }
    }

    public static string WriteFile(string filename, string content, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Expand environment variables in filename
            filename = Environment.ExpandEnvironmentVariables(filename);

            // Ensure the directory exists
            string directory = Path.GetDirectoryName(filename);
            if (!EnsureDirectoryExists(directory, out exitCode, out string errorMessage))
            {
                return errorMessage;
            }

            File.WriteAllText(filename, content, Encoding.UTF8);
            return $"File written successfully: {filename}";
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error writing file: " + ex.Message;
        }
    }

    public static string MoveFile(string sourcePath, string destinationPath, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Validate paths and prepare for file operation
            if (!ValidateFileOperationPaths(ref sourcePath, ref destinationPath, out exitCode, out string errorMessage))
            {
                return errorMessage;
            }

            // Move the file
            File.Move(sourcePath, destinationPath);
            
            return $"File moved successfully from '{sourcePath}' to '{destinationPath}'";
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error moving file: " + ex.Message;
        }
    }

    public static string CopyFile(string sourcePath, string destinationPath, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Validate paths and prepare for file operation
            if (!ValidateFileOperationPaths(ref sourcePath, ref destinationPath, out exitCode, out string errorMessage))
            {
                return errorMessage;
            }

            // Copy the file
            File.Copy(sourcePath, destinationPath);
            
            return $"File copied successfully from '{sourcePath}' to '{destinationPath}'";
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error copying file: " + ex.Message;
        }
    }

    // Shared validation for file operations (move, copy, etc.)
    private static bool ValidateFileOperationPaths(ref string sourcePath, ref string destinationPath, out int exitCode, out string errorMessage)
    {
        exitCode = 0;
        errorMessage = null;

        // Expand environment variables in both paths
        sourcePath = Environment.ExpandEnvironmentVariables(sourcePath);
        destinationPath = Environment.ExpandEnvironmentVariables(destinationPath);

        // Check if source file exists
        if (!File.Exists(sourcePath))
        {
            exitCode = 1;
            errorMessage = $"Source file not found: {sourcePath}";
            return false;
        }

        // Check if destination file already exists
        if (File.Exists(destinationPath))
        {
            exitCode = 1;
            errorMessage = $"Destination file already exists: {destinationPath}";
            return false;
        }

        // Ensure the destination directory exists
        string destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!EnsureDirectoryExists(destinationDirectory, out exitCode, out errorMessage))
        {
            return false;
        }

        return true;
    }

    public static string DeleteFile(string filePath, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Expand environment variables in the path
            filePath = Environment.ExpandEnvironmentVariables(filePath);

            // Check if file exists
            if (!File.Exists(filePath))
            {
                exitCode = 1;
                return $"File not found: {filePath}";
            }

            // Delete the file
            File.Delete(filePath);
            
            return $"File deleted successfully: {filePath}";
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error deleting file: " + ex.Message;
        }
    }

    public static string ListDirectory(string directoryPath, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Expand environment variables in the path
            directoryPath = Environment.ExpandEnvironmentVariables(directoryPath);

            // Check if directory exists
            if (!Directory.Exists(directoryPath))
            {
                exitCode = 1;
                return $"Directory not found: {directoryPath}";
            }

            StringBuilder result = new StringBuilder();
            result.AppendLine($"Contents of: {directoryPath}");
            result.AppendLine();

            // List directories
            string[] directories = Directory.GetDirectories(directoryPath);
            if (directories.Length > 0)
            {
                result.AppendLine("Directories:");
                foreach (string dir in directories)
                {
                    string dirName = Path.GetFileName(dir);
                    result.AppendLine($"  [DIR]  {dirName}");
                }
                result.AppendLine();
            }

            // List files
            string[] files = Directory.GetFiles(directoryPath);
            if (files.Length > 0)
            {
                result.AppendLine("Files:");
                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    FileInfo fileInfo = new FileInfo(file);
                    long fileSizeBytes = fileInfo.Length;
                    string fileSize = FormatFileSize(fileSizeBytes);
                    result.AppendLine($"  [FILE] {fileName} ({fileSize})");
                }
            }

            if (directories.Length == 0 && files.Length == 0)
            {
                result.AppendLine("Directory is empty.");
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error listing directory: " + ex.Message;
        }
    }

    public static string ExtractFile(string archivePath, string destinationPath, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Expand environment variables in both paths
            archivePath = Environment.ExpandEnvironmentVariables(archivePath);
            destinationPath = Environment.ExpandEnvironmentVariables(destinationPath);

            // Check if archive exists
            if (!File.Exists(archivePath))
            {
                exitCode = 1;
                return $"Archive not found: {archivePath}";
            }

            // Ensure the destination directory exists
            if (!EnsureDirectoryExists(destinationPath, out exitCode, out string errorMessage))
            {
                return errorMessage;
            }

            // Build 7za.exe arguments: x (extract with full paths) -o (output directory) -y (yes to all prompts)
            string arguments = $"x \"{archivePath}\" -o\"{destinationPath}\" -y";

            string output = ToolHandler.ExecuteProcess("7za.exe", arguments, out exitCode);

            if (exitCode != 0)
            {
                return $"7za exited with code {exitCode}:\n{output}";
            }

            return $"Archive extracted successfully to: {destinationPath}\n{output}";
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error running 7za.exe: " + ex.Message;
        }
    }

    // Helper method to ensure a directory exists
    public static bool EnsureDirectoryExists(string directoryPath, out int exitCode, out string errorMessage)
    {
        exitCode = 0;
        errorMessage = null;

        if (string.IsNullOrEmpty(directoryPath) || Directory.Exists(directoryPath))
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(directoryPath);
            return true;
        }
        catch (Exception ex)
        {
            exitCode = 1;
            errorMessage = $"Failed to create directory '{directoryPath}': {ex.Message}";
            return false;
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

