using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace WebTools
{
    public static class SearchHandler
    {
        // Delegate for parsing search results from raw response
        private delegate string ResultParser(string response, out int exitCode);

        // Generic search template - executes curl and parses results
        private static string ExecuteSearch(string url, ResultParser parser, out int exitCode, int maxSearchResults, params string[] headers)
        {
            exitCode = 0;
            string response = "";

            // Execute curl request
            try
            {
                response = CurlHelper.Execute(url, out exitCode, combineErrorOutput: false, extraHeaders: headers);
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
                string parsed = parser(response, out exitCode);

                // Truncate to max search results
                string[] lines = parsed.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > maxSearchResults)
                    parsed = string.Join("\n", lines, 0, maxSearchResults) + "\n";

                return parsed;
            }
            catch
            {
                exitCode = -1;
                return "";
            }
        }

        // Searches the web with DuckDuckGo
        public static string RunDDGSearch(string query, int maxSearchResults, out int exitCode)
        {
            string url = "https://duckduckgo.com/html/?q=" + HttpUtility.UrlEncode(query);
            return ExecuteSearch(url, ParseDDGResults, out exitCode, maxSearchResults,
                "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                "Accept-Language: en-US,en;q=0.5");
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
        public static string RunWibySearch(string query, int maxSearchResults, out int exitCode)
        {
            string url = "https://wiby.me/json/?q=" + HttpUtility.UrlEncode(query);
            return ExecuteSearch(url, ParseWibyResults, out exitCode, maxSearchResults);
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
        public static string RunSearXNGSearch(string query, string searxngInstance, int maxSearchResults, out int exitCode)
        {
            string url = searxngInstance + "/search?q=" + HttpUtility.UrlEncode(query) + "&format=json";
            return ExecuteSearch(url, ParseSearXNGResults, out exitCode, maxSearchResults);
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
}
