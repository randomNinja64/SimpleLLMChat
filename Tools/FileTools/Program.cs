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
                            int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "offset")?.Trim() ?? "", out int offset);
                            int maxContentLength = ToolHelper.GetConfigInt("maxfilecontentlength", 8000);
                            int exitCode;
                            string output = FileHandler.ReadFile(filename, maxContentLength, out exitCode, offset);
                            return new ToolResult(output, exitCode);
                        }

                    case "write_file":
                        {
                            string filename = ToolHelper.GetRequiredArg(argumentsJson, "filename");
                            string content = ToolHelper.JsonExtractString(argumentsJson, "content")?.Trim() ?? "";
                            int exitCode;
                            string output = FileHandler.WriteFile(filename, content, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    case "edit_file":
                        {
                            string filename = ToolHelper.GetRequiredArg(argumentsJson, "filename");
                            JArray edits = ToolHelper.JsonExtractArray(argumentsJson, "edits");
                            if (edits == null || edits.Count == 0)
                                return ToolHelper.Fail("missing or empty 'edits' argument.");

                            int exitCode;
                            string output = EditFileTool.Apply(filename, edits, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    case "extract_file":
                        {
                            string archivePath = ToolHelper.GetRequiredArg(argumentsJson, "archive_path");
                            string destinationPath = ToolHelper.GetRequiredArg(argumentsJson, "destination_path");
                            int exitCode;
                            string output = FileHandler.ExtractFile(archivePath, destinationPath, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    case "move_file":
                        {
                            string sourcePath = ToolHelper.GetRequiredArg(argumentsJson, "source_path");
                            string destinationPath = ToolHelper.GetRequiredArg(argumentsJson, "destination_path");
                            int exitCode;
                            string output = FileHandler.MoveFile(sourcePath, destinationPath, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    case "copy_file":
                        {
                            string sourcePath = ToolHelper.GetRequiredArg(argumentsJson, "source_path");
                            string destinationPath = ToolHelper.GetRequiredArg(argumentsJson, "destination_path");
                            int exitCode;
                            string output = FileHandler.CopyFile(sourcePath, destinationPath, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    case "delete_file":
                        {
                            string filePath = ToolHelper.GetRequiredArg(argumentsJson, "file_path");
                            int exitCode;
                            string output = FileHandler.DeleteFile(filePath, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    case "list_directory":
                        {
                            string directoryPath = ToolHelper.GetRequiredArg(argumentsJson, "directory_path");
                            int exitCode;
                            string output = FileHandler.ListDirectory(directoryPath, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    case "grep_search":
                        {
                            string pattern = ToolHelper.GetRequiredArg(argumentsJson, "pattern");
                            string directoryPath = ToolHelper.JsonExtractString(argumentsJson, "directory_path");
                            string filePattern = ToolHelper.JsonExtractString(argumentsJson, "file_pattern");
                            string caseInsensitive = ToolHelper.JsonExtractString(argumentsJson, "case_insensitive");
                            int maxContentLength = ToolHelper.GetConfigInt("maxfilecontentlength", 8000);
                            int exitCode;
                            string output = GrepSearchTool.Search(pattern, directoryPath, filePattern, caseInsensitive, maxContentLength, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    default:
                        return ToolHelper.Fail("unknown tool '" + toolName + "'.");
                }
            });
        }
    }
}
