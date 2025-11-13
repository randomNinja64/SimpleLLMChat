using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
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

    private static string FormatCommandResult(string command, string output, int exitCode)
    {
        return $"Command: {command}\nExit Code: {exitCode}\nOutput:\n{output}";
    }
}
