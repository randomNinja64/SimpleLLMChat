using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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

        if (call.Name == "download_file")
        {
            // Expected arguments payload should contain {"filename": "...", "content": "..."} as JSON.
            string filename = Trim(JsonExtractString(call.Arguments, "filename"));
            string URL = Trim(JsonExtractString(call.Arguments, "URL"));

            if (string.IsNullOrEmpty(URL))
            {
                toolContent = "error: missing 'URL' argument for download_file.";
                return true;
            }

            try
            {
                string output = DownloadFile(filename, URL, out exitCode);
                toolContent = FormatCommandResult("download file: " + URL, output, exitCode);
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

        if (call.Name == "extract_file")
        {
            // Expected arguments payload should contain {"archive_path": "...", "destination_path": "..."} as JSON.
            string archivePath = Trim(JsonExtractString(call.Arguments, "archive_path"));
            string destinationPath = Trim(JsonExtractString(call.Arguments, "destination_path"));

            if (string.IsNullOrEmpty(archivePath))
            {
                toolContent = "error: missing 'archive_path' argument for extract_file.";
                return true;
            }

            if (string.IsNullOrEmpty(destinationPath))
            {
                toolContent = "error: missing 'destination_path' argument for extract_file.";
                return true;
            }

            try
            {
                string output = ExtractFile(archivePath, destinationPath, out exitCode);
                toolContent = FormatCommandResult("extract file: " + archivePath, output, exitCode);
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

    // Searches the web with DuckDuckGo, falls back to Wiby if DDG fails or returns no results
    private static string RunWebSearch(string query, out int exitCode)
    {
        exitCode = 0;
        string html = "";

        // Try DuckDuckGo first
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
            // DDG failed, try Wiby
            return RunWibySearch(query, out exitCode);
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

        // If DDG returned no results, try Wiby
        if (results.Length == 0)
        {
            return RunWibySearch(query, out exitCode);
        }

        return results.ToString();
    }

    // Searches the web with Wiby using their JSON API
    private static string RunWibySearch(string query, out int exitCode)
    {
        exitCode = 0;
        string json = "";

        // Run CURL to get JSON results from Wiby
        try
        {
            // Build curl command arguments for JSON API
            string arguments = "-s -L \"https://wiby.me/json/?q=" + HttpUtility.UrlEncode(query) + "\" " +
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
                json = reader.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
            }
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error running curl.exe for search: " + ex.Message;
        }

        // Parse JSON response (Wiby returns an array at the root level)
        try
        {
            JArray resultsArray = JArray.Parse(json);

            StringBuilder results = new StringBuilder();
            if (resultsArray == null || resultsArray.Count == 0)
            {
                return "No results found.";
            }

            foreach (JToken result in resultsArray)
            {
                string url = result["URL"]?.ToString() ?? "";
                string title = result["Title"]?.ToString() ?? "";
                string snippet = result["Snippet"]?.ToString() ?? "";

                if (!string.IsNullOrEmpty(url))
                {
                    results.AppendLine(url + " : " + title + " - " + snippet);
                }
            }

            if (results.Length == 0)
            {
                return "No results found.";
            }

            return results.ToString();
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error parsing JSON: " + ex.Message;
        }
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
            html = Regex.Replace(html, @"</?li\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
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
            // Truncate to 8000 characters
            if (html.Length > 8000)
                html = html.Substring(0, 8000);
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

    private static string DownloadFile(string filename, string URL, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Expand environment variables in filename
            filename = Environment.ExpandEnvironmentVariables(filename);

            // Get expected MIME types based on file extension
            string fileExtension = Path.GetExtension(filename).ToLower();
            string[] expectedTypes = GetExpectedMimeTypes(fileExtension);

            string contentType = "";

            // Only perform HEAD request if we have expected MIME types to validate
            if (expectedTypes != null)
            {
                contentType = GetContentTypeFromURL(URL, out int headExitCode);
                
                // If HEAD request succeeds, validate the content type
                if (headExitCode == 0 && !string.IsNullOrEmpty(contentType))
                {
                    // Verify content type matches expected type
                    bool isValidType = false;
                    foreach (string expectedType in expectedTypes)
                    {
                        if (contentType.ToLower().Contains(expectedType.ToLower()))
                        {
                            isValidType = true;
                            break;
                        }
                    }

                    if (!isValidType)
                    {
                        exitCode = 1;
                        return $"File type mismatch: Expected {string.Join(" or ", expectedTypes)} but got '{contentType}'. Download cancelled.";
                    }
                }
                // If HEAD request fails, proceed with download anyway
            }

            // Ensure directory exists
            string directory = Path.GetDirectoryName(filename);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Build curl arguments
            string arguments =
                "-L -s " +
                "-H \"User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.5993.90 Safari/537.36\" " +
                "-o \"" + filename + "\" " +
                "\"" + URL + "\"";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            string stdOut = "";
            string stdErr = "";

            using (Process process = Process.Start(psi))
            {
                stdOut = process.StandardOutput.ReadToEnd();
                stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
            }

            if (exitCode != 0)
            {
                return $"curl exited with code {exitCode}: {stdErr}";
            }

            string successMessage = $"File downloaded successfully: {filename}";
            if (!string.IsNullOrEmpty(contentType))
            {
                successMessage += $" (Content-Type: {contentType})";
            }

            return successMessage;
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error running curl.exe: " + ex.Message;
        }
    }

    private static string GetContentTypeFromURL(string URL, out int exitCode)
    {
        exitCode = 0;
        
        try
        {
            // Build curl HEAD request arguments
            string arguments =
                "-I -L -s " +
                "-H \"User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.5993.90 Safari/537.36\" " +
                "\"" + URL + "\"";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            string stdOut = "";
            
            using (Process process = Process.Start(psi))
            {
                stdOut = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
            }

            if (exitCode != 0)
            {
                return "";
            }

            // Extract Content-Type from headers
            Regex contentTypeRegex = new Regex(@"content-type:\s*([^\r\n;]+)", RegexOptions.IgnoreCase);
            Match match = contentTypeRegex.Match(stdOut);
            
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            return "";
        }
        catch (Exception)
        {
            exitCode = -1;
            return "";
        }
    }

    private static readonly Dictionary<string, string[]> MimeTypeMap = new Dictionary<string, string[]>()
    {
        // Images
        { ".jpg", new[] { "image/jpeg" } },
        { ".jpeg", new[] { "image/jpeg" } },
        { ".png", new[] { "image/png" } },
        { ".gif", new[] { "image/gif" } },
        { ".webp", new[] { "image/webp" } },
        { ".bmp", new[] { "image/bmp" } },
        { ".svg", new[] { "image/svg+xml" } },
        { ".ico", new[] { "image/x-icon", "image/vnd.microsoft.icon" } },

        // Documents
        { ".pdf", new[] { "application/pdf" } },
        { ".doc", new[] { "application/msword" } },
        { ".docx", new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" } },
        { ".xls", new[] { "application/vnd.ms-excel" } },
        { ".xlsx", new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } },
        { ".ppt", new[] { "application/vnd.ms-powerpoint" } },
        { ".pptx", new[] { "application/vnd.openxmlformats-officedocument.presentationml.presentation" } },

        // Text files
        { ".txt", new[] { "text/plain" } },
        { ".html", new[] { "text/html" } },
        { ".htm", new[] { "text/html" } },
        { ".css", new[] { "text/css" } },
        { ".js", new[] { "text/javascript", "application/javascript" } },
        { ".json", new[] { "application/json" } },
        { ".xml", new[] { "text/xml", "application/xml" } },
        { ".csv", new[] { "text/csv" } },

        // Archives
        { ".zip", new[] { "application/zip" } },
        { ".rar", new[] { "application/x-rar-compressed" } },
        { ".7z", new[] { "application/x-7z-compressed" } },
        { ".tar", new[] { "application/x-tar" } },
        { ".gz", new[] { "application/gzip" } },

        // Audio
        { ".mp3", new[] { "audio/mpeg" } },
        { ".wav", new[] { "audio/wav" } },
        { ".ogg", new[] { "audio/ogg" } },
        { ".flac", new[] { "audio/flac" } },

        // Video
        { ".mp4", new[] { "video/mp4" } },
        { ".avi", new[] { "video/x-msvideo" } },
        { ".mkv", new[] { "video/x-matroska" } },
        { ".mov", new[] { "video/quicktime" } },
        { ".webm", new[] { "video/webm" } },

        // Executables and binaries
        { ".exe", new[] { "application/x-msdownload", "application/octet-stream" } },
        { ".dll", new[] { "application/x-msdownload", "application/octet-stream" } },
        { ".bin", new[] { "application/octet-stream" } }
    };

    private static string[] GetExpectedMimeTypes(string fileExtension)
    {
        string[] result;
        if (MimeTypeMap.TryGetValue(fileExtension.ToLower(), out result))
        {
            return result;
        }
        return null;
    }

    private static string ReadFile(string filename, out int exitCode, int maxLength = 8000)
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
            // Expand environment variables in filename
            filename = Environment.ExpandEnvironmentVariables(filename);

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

    private static string ExtractFile(string archivePath, string destinationPath, out int exitCode)
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
            if (!Directory.Exists(destinationPath))
            {
                try
                {
                    Directory.CreateDirectory(destinationPath);
                }
                catch (Exception ex)
                {
                    exitCode = 1;
                    return $"Failed to create destination directory '{destinationPath}': {ex.Message}";
                }
            }

            // Build 7za.exe arguments: x (extract with full paths) -o (output directory) -y (yes to all prompts)
            string arguments = $"x \"{archivePath}\" -o\"{destinationPath}\" -y";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "7za.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            string stdOut = "";
            string stdErr = "";

            using (Process process = Process.Start(psi))
            {
                stdOut = process.StandardOutput.ReadToEnd();
                stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
            }

            if (exitCode != 0)
            {
                return $"7za exited with code {exitCode}:\n{stdErr}\n{stdOut}";
            }

            return $"Archive extracted successfully to: {destinationPath}\n{stdOut}";
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error running 7za.exe: " + ex.Message;
        }
    }



    private static string FormatCommandResult(string command, string output, int exitCode)
    {
        return $"Command: {command}\nExit Code: {exitCode}\nOutput:\n{output}";
    }
}
