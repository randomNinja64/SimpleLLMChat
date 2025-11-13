using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
public static class ToolHandler
{
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

        if (call.Name == "run_shell_command")
        {
            // Expected arguments payload should contain {"command": "..."} as JSON.
            string command = Trim(JsonExtractString(call.Arguments, "command"));
            if (string.IsNullOrEmpty(command))
            {
                toolContent = "error: missing 'command' argument for run_shell_command.";
                return true;
            }

            try
            {
                string output = RunShellCommand(command, out exitCode);
                toolContent = FormatCommandResult(command, output, exitCode);
            }
            catch (Exception e)
            {
                toolContent = "error: " + e.Message;
            }

            return true;
        }

        if (call.Name == "read_website")
        {
            // Expected arguments payload should contain {"URL": "..."} as JSON.
            string URL = Trim(JsonExtractString(call.Arguments, "URL"));
            if (string.IsNullOrEmpty(URL))
            {
                toolContent = "error: missing 'URL' argument for read_website.";
                return true;
            }

            try
            {
                string output = ReadWebsite(URL, out exitCode);
                toolContent = FormatCommandResult("read website: " + URL, output, exitCode);
            }
            catch (Exception e)
            {
                toolContent = "error: " + e.Message;
            }

            return true;
        }

        if (call.Name == "run_web_search")
        {
            // Expected arguments payload should contain {"query": "..."} as JSON.
            string query = Trim(JsonExtractString(call.Arguments, "query"));
            if (string.IsNullOrEmpty(query))
            {
                toolContent = "error: missing 'query' argument for run_web_search.";
                return true;
            }

            try
            {
                string output = RunWebSearch(query, out exitCode);
                toolContent = FormatCommandResult("web search: " + query, output, exitCode);
            }
            catch (Exception e)
            {
                toolContent = "error: " + e.Message;
            }

            return true;
        }

        if (call.Name == "download_video")
        {
            // Expected arguments payload should contain {"URL": "..."} as JSON.
            string URL = Trim(JsonExtractString(call.Arguments, "URL"));
            if (string.IsNullOrEmpty(URL))
            {
                toolContent = "error: missing 'URL' argument for download_video.";
                return true;
            }

            try
            {
                string output = DownloadVideo(URL, out exitCode);
                toolContent = FormatCommandResult("download video: " + URL, output, exitCode);
            }
            catch (Exception e)
            {
                toolContent = "error: " + e.Message;
            }

            return true;
        }

        if (call.Name == "read_file")
        {
            // Expected arguments payload should contain {"filename": "..."} as JSON.
            string filename = Trim(JsonExtractString(call.Arguments, "filename"));
            if (string.IsNullOrEmpty(filename))
            {
                toolContent = "error: missing 'filename' argument for read_file.";
                return true;
            }

            try
            {
                string output = ReadFile(filename, out exitCode);
                toolContent = FormatCommandResult("read file: " + filename, output, exitCode);
            }
            catch (Exception e)
            {
                toolContent = "error: " + e.Message;
            }

            return true;
        }

        if (call.Name == "write_file")
        {
            // Expected arguments payload should contain {"filename": "...", "content": "..."} as JSON.
            string filename = Trim(JsonExtractString(call.Arguments, "filename"));
            string content = Trim(JsonExtractString(call.Arguments, "content"));

            if (string.IsNullOrEmpty(filename))
            {
                toolContent = "error: missing 'filename' argument for write_file.";
                return true;
            }

            if (content == null)
            {
                toolContent = "error: missing 'content' argument for write_file.";
                return true;
            }

            try
            {
                string output = WriteFile(filename, content, out exitCode);
                toolContent = FormatCommandResult("write file: " + filename, output, exitCode);
            }
            catch (Exception e)
            {
                toolContent = "error: " + e.Message;
            }

            return true;
        }

        toolContent = $"error: unknown tool '{call.Name}'.";
        return false;
    }

    // Placeholder helper methods
    private static string Trim(string s) => s?.Trim() ?? "";

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
        var output = new StringBuilder();
        exitCode = 0;

        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "cmd.exe"; // For Windows shell
                process.StartInfo.Arguments = "/c " + command; // /c executes the command and exits
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false; // Required to redirect output
                process.StartInfo.CreateNoWindow = true;

                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        output.AppendLine(args.Data);
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        output.AppendLine(args.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                exitCode = process.ExitCode;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to execute command.", ex);
        }

        return output.ToString();
    }

    // Searches the web with DuckDuckGo
    private static string RunWebSearch(string query, out int exitCode)
    {
        exitCode = 0;
        string html = "";

        // Run CURL
        try
        {
            // Build curl command arguments
            string arguments = "-s -L \"https://duckduckgo.com/html/?q=" + HttpUtility.UrlEncode(query) + "\" " +
                               "-H \"User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.5993.90 Safari/537.36\"";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using (Process process = Process.Start(psi))
            using (StreamReader reader = process.StandardOutput)
            {
                html = reader.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
            }
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error running curl.exe: " + ex.Message;
        }

        // Parse result snippets
        Regex snippetRegex = new Regex("<a class=\"result__snippet\" href=\"([^\"]+)\">(.+?)</a>", RegexOptions.IgnoreCase);
        Regex htmlTagRegex = new Regex("<[^>]+>");
        Regex uddgRegex = new Regex("uddg=([^&]+)");

        MatchCollection matches = snippetRegex.Matches(html);
        StringBuilder results = new StringBuilder();

        foreach (Match match in matches)
        {
            string href = match.Groups[1].Value;
            string snippet = match.Groups[2].Value;

            // Remove HTML tags from snippet
            snippet = htmlTagRegex.Replace(snippet, "");

            // Extract the actual URL from the uddg parameter
            Match urlMatch = uddgRegex.Match(href);
            if (urlMatch.Success)
            {
                string fixedUrl = HttpUtility.UrlDecode(urlMatch.Groups[1].Value);
                
                // Skip ads (URLs containing duckduckgo.com/y.js are ad tracking links)
                if (!fixedUrl.Contains("duckduckgo.com/y.js"))
                {
                    results.AppendLine(fixedUrl + " : " + snippet);
                }
            }
        }

        return results.ToString();
    }

    private static string ReadWebsite(string URL, out int exitCode)
    {
        exitCode = 0;
        string html = "";

        try
        {
            // Build curl command arguments
            string arguments = "-s -L \"" + URL + "\" " +
                               "-H \"User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.5993.90 Safari/537.36\"";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using (Process process = Process.Start(psi))
            using (StreamReader reader = process.StandardOutput)
            {
                html = reader.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
            }

            // Strip out <script> and <style> blocks
            html = Regex.Replace(html, @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<style\b[^<]*(?:(?!<\/style>)<[^<]*)*<\/style>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<path\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<svg\b[^<]*(?:(?!<\/svg>)<[^<]*)*<\/svg>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<img\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<meta\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<link\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Optionally remove inline JS/CSS in attributes like onclick, style etc.
            html = Regex.Replace(html, @"\s(on\w+|style|class|id|method|role)\s*=\s*(['""]).*?\2", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);
            html = Regex.Replace(html, @"^\s*$[\r\n]*", "", RegexOptions.Multiline);

            // Truncate to 4000 characters
            if (html.Length > 4000)
                html = html.Substring(0, 4000);
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error running curl.exe: " + ex.Message;
        }

        return html+"\n";
    }

    private static string DownloadVideo(string URL, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Get the user's desktop path
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            // Use an explicit output template in the desktop folder so yt-dlp handles the file naming
            string outputTemplate = Path.Combine(desktopPath, "%(title)s.%(ext)s");

            // Build arguments: no progress, output template, then the URL (each argument quoted)
            string arguments = $"--no-progress -o \"{outputTemplate}\" \"{URL}\"";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "yt-dlp.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using (Process process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;

                if (exitCode != 0)
                    return $"yt-dlp exited with code {exitCode}:\n{error}";

                return output;
            }
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error running yt-dlp.exe: " + ex.Message;
        }
    }

    private static string ReadFile(string filename, out int exitCode, int maxLength = 8000)
    {
        exitCode = 0;

        try
        {
            if (!File.Exists(filename))
            {
                exitCode = 1;
                return $"File not found: {filename}";
            }

            string content = File.ReadAllText(filename, Encoding.UTF8);

            if (content.Length > maxLength)
            {
                content = content.Substring(0, maxLength) + "\n...[truncated]";
            }

            return content;
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error reading file: " + ex.Message;
        }
    }

    private static string WriteFile(string filename, string content, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Ensure the directory exists
            string directory = Path.GetDirectoryName(filename);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
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



    private static string FormatCommandResult(string command, string output, int exitCode)
    {
        return $"Command: {command}\nExit Code: {exitCode}\nOutput:\n{output}";
    }
}
