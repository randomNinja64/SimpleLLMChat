using Newtonsoft.Json.Linq;
using SimpleLLMChatCLI;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

public static class SearchHandler
{
    // Delegate for parsing search results from raw response
    private delegate string ResultParser(string response, out int exitCode);

    // Generic search template - executes curl and parses results
    private static string ExecuteSearch(string url, ResultParser parser, out int exitCode)
    {
        exitCode = 0;
        string response = "";

        // Execute curl request
        try
        {
            string arguments = $"-s -L \"{url}\" -H \"User-Agent: {ToolHandler.USER_AGENT}\"";
            response = ToolHandler.ExecuteProcess("curl.exe", arguments, out exitCode, combineErrorOutput: false);
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error running curl.exe for search: " + ex.Message;
        }

        // Check for empty response
        if (string.IsNullOrWhiteSpace(response))
        {
            exitCode = -1;
            return "";
        }

        // Parse the response using the provided parser
        try
        {
            return parser(response, out exitCode);
        }
        catch
        {
            exitCode = -1;
            return "";
        }
    }

    // Searches the web with DuckDuckGo
    public static string RunDDGSearch(string query, out int exitCode)
    {
        string url = "https://duckduckgo.com/html/?q=" + HttpUtility.UrlEncode(query);
        return ExecuteSearch(url, ParseDDGResults, out exitCode);
    }

    // Parses DuckDuckGo HTML results
    private static string ParseDDGResults(string html, out int exitCode)
    {
        exitCode = 0;

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
        string url = "https://wiby.me/json/?q=" + HttpUtility.UrlEncode(query);
        return ExecuteSearch(url, ParseWibyResults, out exitCode);
    }

    // Parses Wiby JSON results (array at root level)
    private static string ParseWibyResults(string json, out int exitCode)
    {
        exitCode = 0;

        JArray resultsArray = JArray.Parse(json);

        StringBuilder results = new StringBuilder();
        if (resultsArray == null || resultsArray.Count == 0)
        {
            return "";
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
            return "";
        }

        return results.ToString();
    }

    // Searches the web with SearXNG
    public static string RunSearXNGSearch(string query, out int exitCode)
    {
        string url = Program.SEARXNG_INSTANCE + "/search?q=" + HttpUtility.UrlEncode(query) + "&format=json";
        return ExecuteSearch(url, ParseSearXNGResults, out exitCode);
    }

    // Parses SearXNG JSON results (object with "results" array)
    private static string ParseSearXNGResults(string json, out int exitCode)
    {
        exitCode = 0;

        JArray sngResults = JToken.Parse(json)["results"] as JArray;

        StringBuilder results = new StringBuilder();

        if (sngResults == null)
        {
            return "";
        }

        foreach (JToken result in sngResults)
        {
            string title = result["title"]?.ToString() ?? "";
            string url = result["url"]?.ToString() ?? "";
            string content = result["content"]?.ToString() ?? "";

            if (!string.IsNullOrEmpty(url))
            {
                results.AppendLine(url + " : " + title + " - " + content);
            }
        }

        if (results.Length == 0)
        {
            return "";
        }

        return results.ToString();
    }

}

