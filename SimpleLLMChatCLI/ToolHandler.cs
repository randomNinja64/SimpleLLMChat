using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
public static class ToolHandler
{
    // Common user agent string for HTTP requests
    public const string USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.5993.90 Safari/537.36";

    public struct ToolCall
    {
        public string Id;
        public string Name;
        public string Arguments;

        public ToolCall(string name, string arguments, string id = "")
        {
            Id = id;
            Name = name;
            Arguments = arguments;
        }
    }

    public static bool ExecuteToolCall(ToolCall call, out string toolContent, out int exitCode)
    {
        toolContent = "";
        exitCode = 0;

        try
        {
            switch (call.Name)
            {
                case "run_shell_command":
                    {
                        string command = GetRequiredArg(call.Arguments, "command");
                        string output = RunShellCommand(command, out exitCode);
                        toolContent = FormatCommandResult(command, output, exitCode);
                        return true;
                    }

                case "read_website":
                    {
                        string URL = GetRequiredArg(call.Arguments, "URL");
                        string output = ReadWebsite(URL, out exitCode);
                        toolContent = FormatCommandResult("read website: " + URL, output, exitCode);
                        return true;
                    }

                case "run_web_search":
                    {
                        string query = GetRequiredArg(call.Arguments, "query");
                        string output = "";
                        
                        // If SearXNG instance is set, try it first
                        if (!string.IsNullOrWhiteSpace(SimpleLLMChatCLI.Program.SEARXNG_INSTANCE))
                        {
                            output = SearchHandler.RunSearXNGSearch(query, out exitCode);
                        }
                        
                        // If no results yet, try DDG
                        if (string.IsNullOrWhiteSpace(output) || output.Trim() == "")
                        {
                            output = SearchHandler.RunDDGSearch(query, out exitCode);
                        }

                        // If no results yet, try Wiby
                        if (string.IsNullOrWhiteSpace(output) || output.Trim() == "")
                        {
                            output = SearchHandler.RunWibySearch(query, out exitCode);
                        }
                        
                        // If no results from all 3, set output to "No results found."
                        if (string.IsNullOrWhiteSpace(output) || output.Trim() == "")
                        {
                            output = "No results found.";
                        }
                        
                        toolContent = FormatCommandResult("web search: " + query, output, exitCode);
                        return true;
                    }

                case "download_video":
                    {
                        string URL = GetRequiredArg(call.Arguments, "URL");
                        string output = DownloadHandler.DownloadVideo(URL, out exitCode);
                        toolContent = FormatCommandResult("download video: " + URL, output, exitCode);
                        return true;
                    }

                case "download_file":
                    {
                        string filename = GetRequiredArg(call.Arguments, "filename");
                        string URL = GetRequiredArg(call.Arguments, "URL");
                        string output = DownloadHandler.DownloadFile(filename, URL, out exitCode);
                        toolContent = FormatCommandResult("download file: " + URL, output, exitCode);
                        return true;
                    }

                case "read_file":
                    {
                        string filename = GetRequiredArg(call.Arguments, "filename");
                        int.TryParse(JsonExtractString(call.Arguments, "offset")?.Trim() ?? "", out int offset);
                        string output = FileHandler.ReadFile(filename, out exitCode, offset);
                        toolContent = FormatCommandResult("read file: " + filename, output, exitCode);
                        return true;
                    }

                case "write_file":
                    {
                        string filename = GetRequiredArg(call.Arguments, "filename");
                        string content = JsonExtractString(call.Arguments, "content")?.Trim() ?? "";
                        string output = FileHandler.WriteFile(filename, content, out exitCode);
                        toolContent = FormatCommandResult("write file: " + filename, output, exitCode);
                        return true;
                    }

                case "extract_file":
                    {
                        string archivePath = GetRequiredArg(call.Arguments, "archive_path");
                        string destinationPath = GetRequiredArg(call.Arguments, "destination_path");
                        string output = FileHandler.ExtractFile(archivePath, destinationPath, out exitCode);
                        toolContent = FormatCommandResult("extract file: " + archivePath, output, exitCode);
                        return true;
                    }

                case "move_file":
                    {
                        string sourcePath = GetRequiredArg(call.Arguments, "source_path");
                        string destinationPath = GetRequiredArg(call.Arguments, "destination_path");
                        string output = FileHandler.MoveFile(sourcePath, destinationPath, out exitCode);
                        toolContent = FormatCommandResult("move file: " + sourcePath, output, exitCode);
                        return true;
                    }

                case "copy_file":
                    {
                        string sourcePath = GetRequiredArg(call.Arguments, "source_path");
                        string destinationPath = GetRequiredArg(call.Arguments, "destination_path");
                        string output = FileHandler.CopyFile(sourcePath, destinationPath, out exitCode);
                        toolContent = FormatCommandResult("copy file: " + sourcePath, output, exitCode);
                        return true;
                    }

                case "delete_file":
                    {
                        string filePath = GetRequiredArg(call.Arguments, "file_path");
                        string output = FileHandler.DeleteFile(filePath, out exitCode);
                        toolContent = FormatCommandResult("delete file: " + filePath, output, exitCode);
                        return true;
                    }

                case "list_directory":
                    {
                        string directoryPath = GetRequiredArg(call.Arguments, "directory_path");
                        string output = FileHandler.ListDirectory(directoryPath, out exitCode);
                        toolContent = FormatCommandResult("list directory: " + directoryPath, output, exitCode);
                        return true;
                    }

                case "run_python_script":
                    {
                        string scriptContent = GetRequiredArg(call.Arguments, "script_content");
                        string output = RunPythonScript(scriptContent, out exitCode);
                        toolContent = FormatCommandResult("run python script", output, exitCode);
                        return true;
                    }

                default:
                    toolContent = $"error: unknown tool '{call.Name}'.";
                    return false;
            }
        }
        catch (Exception e)
        {
            toolContent = "error: " + e.Message;
            return true;
        }
    }

    private static string GetRequiredArg(string arguments, string argName)
    {
        string value = JsonExtractString(arguments, argName)?.Trim() ?? "";
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException($"missing '{argName}' argument.");
        return value;
    }

    // Generic process execution helper (public for use by other handlers)
    public static string ExecuteProcess(string fileName, string arguments, out int exitCode, bool combineErrorOutput = true)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (Process process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
                
                // Combine stdout and stderr if requested
                if (combineErrorOutput && !string.IsNullOrEmpty(error))
                {
                    return output + error;
                }
                return output;
            }
        }
        catch (Exception ex)
        {
            exitCode = -1;
            throw new InvalidOperationException($"Failed to execute {fileName}: {ex.Message}", ex);
        }
    }

    private static string JsonExtractString(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
        {
            return "";
        }

        try
        {
            string trimmedJson = json.Trim();
            if (trimmedJson.Length == 0)
            {
                return "";
            }

            JToken root = JToken.Parse(trimmedJson);
            if (root.Type == JTokenType.Object)
            {
                JObject obj = (JObject)root;
                JToken token;
                if (!obj.TryGetValue(key, out token))
                {
                    foreach (JProperty property in obj.Properties())
                    {
                        if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                        {
                            token = property.Value;
                            break;
                        }
                    }
                }

                if (token != null && token.Type != JTokenType.Null)
                {
                    return token.Type == JTokenType.String ? token.Value<string>() ?? "" : token.ToString();
                }
            }
        }
        catch
        {
        }

        return "";
    }

    // Runs shell commands on the OS
    private static string RunShellCommand(string command, out int exitCode)
    {
        return ExecuteProcess("cmd.exe", "/c " + command, out exitCode);
    }

    private static string ReadWebsite(string URL, out int exitCode)
    {
        string html = "";

        try
        {
            // Build curl command arguments
            string arguments = "-s -L \"" + URL + "\" " +
                               "-H \"User-Agent: " + USER_AGENT + "\"";

            html = ExecuteProcess("curl.exe", arguments, out exitCode, combineErrorOutput: false);

            // Strip out DOCTYPE, <script> and <style> blocks
            html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<html\b[^>]*>", "<html>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<style\b[^<]*(?:(?!<\/style>)<[^<]*)*<\/style>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<path\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<svg\b[^<]*(?:(?!<\/svg>)<[^<]*)*<\/svg>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<nav\b[^<]*(?:(?!<\/nav>)<[^<]*)*<\/nav>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<header\b[^<]*(?:(?!<\/header>)<[^<]*)*<\/header>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<meta\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<link\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<form\b[^<]*(?:(?!<\/form>)<[^<]*)*<\/form>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<input\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Strip all attributes from img tags except src
            html = Regex.Replace(html, @"<img\b[^>]*\bsrc\s*=\s*(['""])([^'""]*)\1[^>]*>", "<img src=\"$2\">", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Strip all attributes from a/link tags except href
            html = Regex.Replace(html, @"<a\b[^>]*\bhref\s*=\s*(['""])([^'""]*)\1[^>]*>", "<a href=\"$2\">", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Optionally remove inline JS/CSS in attributes like onclick, style etc.
            html = Regex.Replace(html, @"\s(on\w+|style|class|id|method|role)\s*=\s*(['""]).*?\2", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);
            html = Regex.Replace(html, @"^\s*$[\r\n]*", "", RegexOptions.Multiline);
            html = Regex.Replace(html, @"<head\b[^>]*>.*?</head>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Remove <html> and </html> tags
            html = Regex.Replace(html, @"<html>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</html>", "", RegexOptions.IgnoreCase);
            // Remove <body> and </body> tags
            html = Regex.Replace(html, @"<body\b[^>]*>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</body>", "", RegexOptions.IgnoreCase);
            // Remove p, i, b, u tags but keep their content
            html = Regex.Replace(html, @"</?[pibPIB]\b[^>]*>", "", RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?u\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Remove ul, ol, li tags but keep their content
            html = Regex.Replace(html, @"</?ul\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?ol\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?li\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            // Remove div tags but keep their content
            html = Regex.Replace(html, @"</?div\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Remove strong, span, pre tags but keep their content
            html = Regex.Replace(html, @"</?strong\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?span\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?pre\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Remove table tags but keep their content
            html = Regex.Replace(html, @"</?table\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?thead\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?tbody\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?tfoot\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?tr\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?td\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?th\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Remove whitespace between tags (but keep text content intact)
            html = Regex.Replace(html, @">\s+<", "><", RegexOptions.Singleline);
            // Collapse multiple spaces into single space
            html = Regex.Replace(html, @"[ \t]+", " ", RegexOptions.Multiline);
            // Trim leading/trailing whitespace from each line
            html = Regex.Replace(html, @"^\s+|\s+$", "", RegexOptions.Multiline);
            // Truncate to max content length
            if (html.Length > SimpleLLMChatCLI.Program.MAX_CONTENT_LENGTH)
                html = html.Substring(0, SimpleLLMChatCLI.Program.MAX_CONTENT_LENGTH);
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error running curl.exe: " + ex.Message;
        }

        return html+"\n";
    }

    private static string RunPythonScript(string scriptContent, out int exitCode)
    {
        exitCode = 0;
        string pythonCommand = "";

        try
        {
            // Check if Python is installed by trying common commands
            string[] pythonCommands = { "python", "python3", "py" };
            bool pythonFound = false;

            foreach (string cmd in pythonCommands)
            {
                try
                {
                    string versionOutput = ExecuteProcess(cmd, "--version", out int versionExitCode, combineErrorOutput: true);
                    if (versionExitCode == 0)
                    {
                        pythonCommand = cmd;
                        pythonFound = true;
                        break;
                    }
                }
                catch
                {
                    // Continue to next command
                }
            }

            if (!pythonFound)
            {
                exitCode = 1;
                return "Error: Python runtime not found in system PATH. Please install Python and ensure it's added to your system PATH environment variable.";
            }

            // Create a temporary Python script file
            string tempScriptPath = Path.Combine(Path.GetTempPath(), $"temp_script_{Guid.NewGuid()}.py");

            try
            {
                // Write the script content to the temporary file
                File.WriteAllText(tempScriptPath, scriptContent, Encoding.UTF8);

                // Execute the Python script
                string output = ExecuteProcess(pythonCommand, $"\"{tempScriptPath}\"", out exitCode, combineErrorOutput: true);

                return output;
            }
            finally
            {
                // Clean up the temporary file
                try
                {
                    if (File.Exists(tempScriptPath))
                    {
                        File.Delete(tempScriptPath);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return $"Error executing Python script: {ex.Message}";
        }
    }

    public static string FormatCommandResult(string command, string output, int exitCode)
    {
        return $"Command: {command}\nExit Code: {exitCode}\nOutput:\n{output}";
    }
}
