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

                    default:
                        return new ToolResult("error: unknown tool '" + toolName + "'.", 1);
                }
            });
        }
    }
}
