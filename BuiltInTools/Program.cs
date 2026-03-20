using Newtonsoft.Json.Linq;
using System;
using System.Text;

namespace BuiltInTools
{
    internal class Program
    {
        static int Main(string[] args)
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length < 1)
            {
                Console.Write("Usage: BuiltInTools.exe <tool_name>\nArguments JSON is read from stdin.");
                return 1;
            }

            string toolName = args[0];

            // Read arguments JSON from stdin
            string stdinData = Console.In.ReadToEnd();
            string argumentsJson = "";

            if (!string.IsNullOrWhiteSpace(stdinData))
            {
                try
                {
                    JObject root = JObject.Parse(stdinData);

                    // Extract config if present
                    JObject configObj = root["config"] as JObject;
                    if (configObj != null)
                    {
                        foreach (JProperty prop in configObj.Properties())
                        {
                            ToolHelper.Config[prop.Name] = prop.Value.ToString();
                        }
                    }

                    // Extract arguments if present
                    JToken argsToken = root["arguments"];
                    if (argsToken != null)
                    {
                        argumentsJson = argsToken.ToString();
                    }
                }
                catch
                {
                    // If parsing fails, treat whole stdin as arguments JSON
                    argumentsJson = stdinData;
                }
            }

            int exitCode = 0;
            string output = "";

            try
            {
                switch (toolName)
                {
                    case "run_shell_command":
                        {
                            string command = ToolHelper.GetRequiredArg(argumentsJson, "command");
                            output = ToolHelper.ExecuteProcess("cmd.exe", "/c " + command, out exitCode);
                            break;
                        }

                    case "read_website":
                        {
                            string URL = ToolHelper.GetRequiredArg(argumentsJson, "URL");
                            int maxContentLength = ToolHelper.GetConfigInt("maxcontentlength", 8000);
                            output = WebTools.ReadWebsite(URL, maxContentLength, out exitCode);
                            break;
                        }

                    case "run_web_search":
                        {
                            string query = ToolHelper.GetRequiredArg(argumentsJson, "query");
                            string searxngInstance = ToolHelper.GetConfigString("searxnginstance");
                            int maxSearchResults = ToolHelper.GetConfigInt("maxsearchresults", 20);
                            output = WebTools.RunWebSearch(query, searxngInstance, maxSearchResults, out exitCode);
                            break;
                        }

                    case "download_video":
                        {
                            string URL = ToolHelper.GetRequiredArg(argumentsJson, "URL");
                            output = DownloadHandler.DownloadVideo(URL, out exitCode);
                            break;
                        }

                    case "download_file":
                        {
                            string filename = ToolHelper.GetRequiredArg(argumentsJson, "filename");
                            string URL = ToolHelper.GetRequiredArg(argumentsJson, "URL");
                            output = DownloadHandler.DownloadFile(filename, URL, out exitCode);
                            break;
                        }

                    case "read_file":
                        {
                            string filename = ToolHelper.GetRequiredArg(argumentsJson, "filename");
                            int.TryParse(ToolHelper.JsonExtractString(argumentsJson, "offset")?.Trim() ?? "", out int offset);
                            int maxContentLength = ToolHelper.GetConfigInt("maxcontentlength", 8000);
                            output = FileHandler.ReadFile(filename, maxContentLength, out exitCode, offset);
                            break;
                        }

                    case "write_file":
                        {
                            string filename = ToolHelper.GetRequiredArg(argumentsJson, "filename");
                            string content = ToolHelper.JsonExtractString(argumentsJson, "content")?.Trim() ?? "";
                            output = FileHandler.WriteFile(filename, content, out exitCode);
                            break;
                        }

                    case "extract_file":
                        {
                            string archivePath = ToolHelper.GetRequiredArg(argumentsJson, "archive_path");
                            string destinationPath = ToolHelper.GetRequiredArg(argumentsJson, "destination_path");
                            output = FileHandler.ExtractFile(archivePath, destinationPath, out exitCode);
                            break;
                        }

                    case "move_file":
                        {
                            string sourcePath = ToolHelper.GetRequiredArg(argumentsJson, "source_path");
                            string destinationPath = ToolHelper.GetRequiredArg(argumentsJson, "destination_path");
                            output = FileHandler.MoveFile(sourcePath, destinationPath, out exitCode);
                            break;
                        }

                    case "copy_file":
                        {
                            string sourcePath = ToolHelper.GetRequiredArg(argumentsJson, "source_path");
                            string destinationPath = ToolHelper.GetRequiredArg(argumentsJson, "destination_path");
                            output = FileHandler.CopyFile(sourcePath, destinationPath, out exitCode);
                            break;
                        }

                    case "delete_file":
                        {
                            string filePath = ToolHelper.GetRequiredArg(argumentsJson, "file_path");
                            output = FileHandler.DeleteFile(filePath, out exitCode);
                            break;
                        }

                    case "list_directory":
                        {
                            string directoryPath = ToolHelper.GetRequiredArg(argumentsJson, "directory_path");
                            output = FileHandler.ListDirectory(directoryPath, out exitCode);
                            break;
                        }

                    case "run_python_script":
                        {
                            string scriptContent = ToolHelper.GetRequiredArg(argumentsJson, "script_content");
                            output = PythonTools.RunPythonScript(scriptContent, out exitCode);
                            break;
                        }

                    default:
                        output = "error: unknown tool '" + toolName + "'.";
                        exitCode = 1;
                        break;
                }
            }
            catch (Exception e)
            {
                output = "error: " + e.Message;
                exitCode = 1;
            }

            Console.Write(output);
            return exitCode;
        }
    }
}
