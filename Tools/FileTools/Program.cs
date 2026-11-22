using Newtonsoft.Json.Linq;
using System;

namespace FileTools
{
    internal class Program
    {
        static int Main(string[] args)
        {
            return ToolHelper.RunToolMain(args, (toolName, argumentsJson) =>
            {
                switch (toolName)
                {
                    case "read_file":
                        {
                            string filename = ToolHelper.GetRequiredArg(argumentsJson, "filename");
                            int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "offset")?.Trim() ?? "", out int lineOffset);
                            int maxReadLines = ToolHelper.GetConfigInt("maxreadlines", 500);
                            int maxContentLength = ToolHelper.GetConfigInt("maxfilecontentlength", 10000);
                            return new ToolResult(FileHandler.ReadFile(filename, maxReadLines, maxContentLength, lineOffset), 0);
                        }

                    case "write_file":
                        {
                            string filename = ToolHelper.GetRequiredArg(argumentsJson, "filename");
                            string content = ToolHelper.JsonExtractString(argumentsJson, "content") ?? "";
                            return new ToolResult(FileHandler.WriteFile(filename, content), 0);
                        }

                    case "edit_file":
                        {
                            string filename = ToolHelper.GetRequiredArg(argumentsJson, "filename");
                            JArray edits = ToolHelper.JsonExtractArray(argumentsJson, "edits");
                            if (edits == null || edits.Count == 0)
                                return ToolHelper.Fail("missing or empty 'edits' argument.");

                            return new ToolResult(EditFileTool.Apply(filename, edits), 0);
                        }

                    case "extract_file":
                        {
                            string archivePath = ToolHelper.GetRequiredArg(argumentsJson, "archive_path");
                            string destinationPath = ToolHelper.GetRequiredArg(argumentsJson, "destination_path");
                            return FileHandler.ExtractFile(archivePath, destinationPath);
                        }

                    case "move_file":
                        {
                            string sourcePath = ToolHelper.GetRequiredArg(argumentsJson, "source_path");
                            string destinationPath = ToolHelper.GetRequiredArg(argumentsJson, "destination_path");
                            return new ToolResult(FileHandler.MoveFile(sourcePath, destinationPath), 0);
                        }

                    case "copy_file":
                        {
                            string sourcePath = ToolHelper.GetRequiredArg(argumentsJson, "source_path");
                            string destinationPath = ToolHelper.GetRequiredArg(argumentsJson, "destination_path");
                            return new ToolResult(FileHandler.CopyFile(sourcePath, destinationPath), 0);
                        }

                    case "delete_file":
                        {
                            string filePath = ToolHelper.GetRequiredArg(argumentsJson, "file_path");
                            return new ToolResult(FileHandler.DeleteFile(filePath), 0);
                        }

                    case "list_directory":
                        {
                            string directoryPath = ToolHelper.GetRequiredArg(argumentsJson, "directory_path");
                            return new ToolResult(FileHandler.ListDirectory(directoryPath), 0);
                        }

                    case "grep_search":
                        {
                            string pattern = ToolHelper.GetRequiredArg(argumentsJson, "pattern");
                            string directoryPath = ToolHelper.JsonExtractString(argumentsJson, "directory_path");
                            string filePattern = ToolHelper.JsonExtractString(argumentsJson, "file_pattern");
                            string caseInsensitive = ToolHelper.JsonExtractString(argumentsJson, "case_insensitive");
                            int maxContentLength = ToolHelper.GetConfigInt("maxfilecontentlength", 10000);
                            return GrepSearchTool.Search(pattern, directoryPath, filePattern, caseInsensitive, maxContentLength);
                        }

                    default:
                        return ToolHelper.Fail("unknown tool '" + toolName + "'.");
                }
            });
        }
    }
}
