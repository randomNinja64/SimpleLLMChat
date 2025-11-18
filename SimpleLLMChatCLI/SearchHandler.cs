using Newtonsoft.Json.Linq;
using SimpleLLMChatCLI;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

public static class SearchHandler
{
    // Searches the web with DuckDuckGo
    public static string RunDDGSearch(string query, out int exitCode)
    {
        string html = "";

        // Try DuckDuckGo
        try
        {
            // Build curl command arguments
            string arguments = "-s -L \"https://duckduckgo.com/html/?q=" + HttpUtility.UrlEncode(query) + "\" " +
                               "-H \"User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.5993.90 Safari/537.36\"";

            html = ToolHandler.ExecuteProcess("curl.exe", arguments, out exitCode, combineErrorOutput: false);
        }
        catch (Exception ex)
        {
            // DDG failed, return empty
            exitCode = -1;
            return "";
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

    // Searches the web with Wiby using their JSON API
    public static string RunWibySearch(string query, out int exitCode)
    {
        string json = "";

        // Run CURL to get JSON results from Wiby
        try
        {
            // Build curl command arguments for JSON API
            string arguments = "-s -L \"https://wiby.me/json/?q=" + HttpUtility.UrlEncode(query) + "\" " +
                               "-H \"User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.5993.90 Safari/537.36\"";

            json = ToolHandler.ExecuteProcess("curl.exe", arguments, out exitCode, combineErrorOutput: false);
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

    // Searches the web with SearXNG
    public static string RunSearXNGSearch(string query, out int exitCode)
    {
        string json = "";

        try
        {
            // Build curl command arguments
            string arguments =
                "-s -L \"" + Program.SEARXNG_INSTANCE + "/search?q=" + HttpUtility.UrlEncode(query) + "&format=json" + "\" " +
                "-H \"User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.5993.90 Safari/537.36\"";

            json = ToolHandler.ExecuteProcess("curl.exe", arguments, out exitCode, combineErrorOutput: false);
        }
        catch (Exception ex)
        {
            // Search failed, return empty
            exitCode = -1;
            return "";
        }

        StringBuilder results = new StringBuilder();

        try
        {
            JObject root = JObject.Parse(json);
            JArray sngResults = (JArray)root["results"];

            if (results == null)
            {
                return "";
            }

            foreach (JObject item in sngResults)
            {
                string title = (string)item["title"];
                string url = (string)item["url"];
                string content = (string)item["content"];

                // Null-checks
                if (url == null) continue;

                results.AppendLine(url + " : " + (title ?? ""));

                if (!string.IsNullOrEmpty(content))
                    results.AppendLine("  " + content);
            }
        }
        catch (Exception ex)
        {
            results.AppendLine("Error parsing SearXNG JSON: " + ex.Message);
        }

        return results.ToString();
    }

}

