using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace WebTools
{
    public static class SearchHandler
    {
        // Each engine returns its results as "url : text" lines, or "" when it has none
        // or its response could not be used, so the caller can fall back to the next engine.

        // GET via curl, then parse and truncate.
        private static string ExecuteSearch(string url, Func<string, string> parser, int maxSearchResults, params string[] headers)
        {
            string response = CurlHelper.Execute(url, out _, combineErrorOutput: false, extraHeaders: headers);
            return FinalizeSearch(response, parser, maxSearchResults);
        }

        // Shared parse + line truncate used by GET engines and Firecrawl POST
        private static string FinalizeSearch(string response, Func<string, string> parser, int maxSearchResults)
        {
            if (string.IsNullOrWhiteSpace(response))
                return "";

            try
            {
                string parsed = parser(response);

                string[] lines = parsed.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (maxSearchResults > 0 && lines.Length > maxSearchResults)
                    parsed = string.Join("\n", lines, 0, maxSearchResults) + "\n";

                return parsed;
            }
            catch
            {
                // Not the format this engine normally returns (error page, rate limit, ...).
                return "";
            }
        }

        // Searches the web with DuckDuckGo
        public static string RunDDGSearch(string query, int maxSearchResults)
        {
            string url = "https://duckduckgo.com/html/?q=" + HttpUtility.UrlEncode(query);
            return ExecuteSearch(url, ParseDDGResults, maxSearchResults,
                "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                "Accept-Language: en-US,en;q=0.5");
        }

        // Parses DuckDuckGo HTML results
        private static string ParseDDGResults(string html)
        {
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
        public static string RunWibySearch(string query, int maxSearchResults)
        {
            string url = "https://wiby.me/json/?q=" + HttpUtility.UrlEncode(query);
            return ExecuteSearch(url, ParseWibyResults, maxSearchResults);
        }

        // Parses Wiby JSON results (array at root level)
        private static string ParseWibyResults(string json)
        {
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

        // Searches the web with Firecrawl /v2/search (POST; same parse/truncate path as GET engines)
        public static string RunFirecrawlSearch(string query, string endpoint, string apiKey,
            int maxSearchResults)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return "";

            string searchUrl = endpoint.TrimEnd('/') + "/v2/search";
            JObject payload = new JObject
            {
                ["query"] = query,
                ["limit"] = maxSearchResults > 0 ? maxSearchResults : 20
            };

            string response = CurlHelper.PostJson(
                searchUrl, payload.ToString(Formatting.None), out _,
                combineErrorOutput: false, CurlHelper.FirecrawlAuthHeaders(apiKey));

            return FinalizeSearch(response, ParseFirecrawlResults, maxSearchResults);
        }

        // Parses Firecrawl search JSON (data.web[] with url/title/description)
        private static string ParseFirecrawlResults(string json)
        {
            JObject root = JObject.Parse(json);
            if (root["success"] != null && root["success"].Type == JTokenType.Boolean
                && !(bool)root["success"])
                return "";

            JArray webResults = null;
            JToken data = root["data"];
            if (data is JObject)
                webResults = data["web"] as JArray;
            else if (data is JArray)
                webResults = (JArray)data;

            if (webResults == null || webResults.Count == 0)
                return "";

            StringBuilder results = new StringBuilder();
            foreach (JToken result in webResults)
            {
                string url = (result["url"]?.ToString() ?? "").Trim();
                string title = NormalizeWhitespace(result["title"]?.ToString());
                string content = NormalizeWhitespace(
                    FirstNonEmpty(
                        result["description"]?.ToString(),
                        result["snippet"]?.ToString(),
                        result["content"]?.ToString()));

                if (!string.IsNullOrEmpty(url))
                    results.AppendLine(url + " : " + title + " - " + content);
            }

            if (results.Length == 0)
                return "";

            return results.ToString();
        }

        private static string NormalizeWhitespace(string text)
        {
            return Regex.Replace(text ?? "", @"\s+", " ").Trim();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return "";
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i];
            }
            return "";
        }

        // Searches the web with SearXNG
        public static string RunSearXNGSearch(string query, string searxngInstance, int maxSearchResults)
        {
            string url = searxngInstance + "/search?q=" + HttpUtility.UrlEncode(query) + "&format=json";
            return ExecuteSearch(url, ParseSearXNGResults, maxSearchResults);
        }

        // Parses SearXNG JSON results (object with "results" array)
        private static string ParseSearXNGResults(string json)
        {
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
