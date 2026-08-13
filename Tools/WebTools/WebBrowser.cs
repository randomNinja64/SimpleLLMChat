using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebTools
{
    public static class WebBrowser
    {
        public static string ReadWebsite(string URL, int maxContentLength, int maxLinks,
            string firecrawlEndpoint, string firecrawlApiKey, out int exitCode)
        {
            if (!string.IsNullOrWhiteSpace(firecrawlEndpoint))
            {
                string firecrawlText;
                if (TryReadViaFirecrawl(URL, firecrawlEndpoint, firecrawlApiKey,
                        maxContentLength, maxLinks, out firecrawlText, out exitCode))
                    return firecrawlText + "\n";
            }

            return ReadWebsiteViaCurl(URL, maxContentLength, maxLinks, out exitCode);
        }

        private static string ReadWebsiteViaCurl(string URL, int maxContentLength, int maxLinks, out int exitCode)
        {
            string html = "";

            try
            {
                // Build curl command arguments
                html = CurlHelper.Execute(URL, out exitCode, combineErrorOutput: false);

                if (exitCode != 0 && string.IsNullOrWhiteSpace(html))
                    return "Error fetching URL (curl exit " + exitCode + ").\n";

                string text;
                try
                {
                    text = HtmlToReadableText(html, URL, maxLinks, maxContentLength);
                }
                catch
                {
                    // Conversion failed: fall back to the older HTML stripper so the LLM still gets something.
                    text = FallbackPlainText(html);
                    if (maxContentLength > 0 && text.Length > maxContentLength)
                        text = TruncateBody(text, maxContentLength);
                }

                if (string.IsNullOrWhiteSpace(text))
                    return "No readable content found.\n";

                return text + "\n";
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "Error running curl.exe: " + ex.Message;
            }
        }

        /// <summary>
        /// Scrapes via Firecrawl /v2/scrape. Returns false on any failure so caller can fall back to curl.
        /// </summary>
        private static bool TryReadViaFirecrawl(string pageUrl, string endpoint, string apiKey,
            int maxContentLength, int maxLinks, out string text, out int exitCode)
        {
            text = null;
            exitCode = -1;

            try
            {
                string scrapeUrl = endpoint.TrimEnd('/') + "/v2/scrape";
                JObject payload = new JObject
                {
                    ["url"] = pageUrl,
                    ["formats"] = new JArray("markdown"),
                    ["onlyMainContent"] = true
                };

                string response = CurlHelper.PostJson(
                    scrapeUrl, payload.ToString(Formatting.None), out exitCode,
                    combineErrorOutput: false, CurlHelper.FirecrawlAuthHeaders(apiKey));

                if (exitCode != 0 || string.IsNullOrWhiteSpace(response))
                    return false;

                JObject root = JObject.Parse(response);
                if (root["success"] != null && root["success"].Type == JTokenType.Boolean
                    && !(bool)root["success"])
                    return false;

                JObject data = root["data"] as JObject;
                if (data == null)
                    return false;

                string markdown = data["markdown"]?.ToString();
                if (string.IsNullOrWhiteSpace(markdown))
                    return false;

                JObject metadata = data["metadata"] as JObject;
                string title = "";
                string desc = "";
                if (metadata != null)
                {
                    title = FirstNonEmpty(
                        metadata["title"]?.ToString(),
                        metadata["ogTitle"]?.ToString());
                    desc = FirstNonEmpty(
                        metadata["description"]?.ToString(),
                        metadata["ogDescription"]?.ToString());
                }

                List<KeyValuePair<string, string>> links;
                string body = MarkdownToReadableBody(markdown, maxLinks, out links);
                if (string.IsNullOrWhiteSpace(body) && links.Count == 0)
                    return false;

                text = AssembleOutput(title, desc, body, links, maxContentLength);
                exitCode = 0;
                return !string.IsNullOrWhiteSpace(text);
            }
            catch
            {
                text = null;
                exitCode = -1;
                return false;
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return "";
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i].Trim();
            }
            return "";
        }

        /// <summary>
        /// Keep images inline (rewritten to [Image: alt](src)); move [text](url) into Links and leave text in body.
        /// </summary>
        private static string MarkdownToReadableBody(string markdown, int maxLinks,
            out List<KeyValuePair<string, string>> links)
        {
            List<KeyValuePair<string, string>> collected = new List<KeyValuePair<string, string>>();
            HashSet<string> seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string body = markdown ?? "";

            // Strip normal links first (not images: (?<!!)); leave label in body
            body = Regex.Replace(body,
                @"(?<!!)\[([^\]]*)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)",
                m => CollectLinkBodyLabel(
                    collected, seenUrls, maxLinks,
                    NormalizeWhitespace(m.Groups[1].Value),
                    m.Groups[2].Value.Trim()));

            // Images stay inline, rewritten to match the curl HTML path
            body = Regex.Replace(body,
                @"!\[([^\]]*)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)",
                m => FormatImageMarkup(
                    NormalizeWhitespace(m.Groups[1].Value),
                    m.Groups[2].Value.Trim()));

            body = Regex.Replace(body, @"\n{3,}", "\n\n");
            links = collected;
            return body.Trim();
        }

        /// <summary>
        /// Converts HTML to a compact readable page: Title/Desc header, body text (image alt+src kept,
        /// link URLs moved to a capped Links section), truncated to fit maxContentLength.
        /// </summary>
        private static string HtmlToReadableText(string html, string pageUrl, int maxLinks, int maxContentLength)
        {
            string title = "";
            Match titleMatch = Regex.Match(html, @"<title\b[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (titleMatch.Success)
                title = NormalizeWhitespace(StripTags(HttpUtility.HtmlDecode(titleMatch.Groups[1].Value)));

            string desc = ExtractMetaDescription(html);

            html = StripNonContentBlocks(html);

            Uri baseUri = null;
            if (!string.IsNullOrWhiteSpace(pageUrl))
                Uri.TryCreate(pageUrl, UriKind.Absolute, out baseUri);

            List<KeyValuePair<string, string>> links = new List<KeyValuePair<string, string>>();
            HashSet<string> seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Links: keep label in body, collect URL for Links section (first-N after dedupe)
            html = Regex.Replace(html,
                @"<a\b[^>]*\bhref\s*=\s*(['""])(.*?)\1[^>]*>(.*?)</a>",
                m =>
                {
                    string href = ResolveUrl(baseUri, HttpUtility.HtmlDecode(m.Groups[2].Value.Trim()));
                    string label = NormalizeWhitespace(StripTags(HttpUtility.HtmlDecode(m.Groups[3].Value)));
                    return CollectLinkBodyLabel(links, seenUrls, maxLinks, label, href);
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Images → [Image: alt](src) so the LLM can download by URL
            html = Regex.Replace(html,
                @"<img\b([^>]*)/?>",
                m => FormatImage(m.Groups[1].Value, baseUri),
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Headings
            for (int level = 1; level <= 6; level++)
            {
                string hashes = new string('#', level);
                html = Regex.Replace(html,
                    @"<h" + level + @"\b[^>]*>(.*?)</h" + level + @">",
                    m => "\n\n" + hashes + " " + NormalizeWhitespace(StripTags(HttpUtility.HtmlDecode(m.Groups[1].Value))) + "\n\n",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }

            // Lists
            html = Regex.Replace(html, @"<li\b[^>]*>", "\n- ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</li\s*>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</?[uo]l\b[^>]*>", "\n", RegexOptions.IgnoreCase);

            // Tables: cells become "| cell "
            html = Regex.Replace(html, @"<t[dh]\b[^>]*>", "| ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</t[dh]\s*>", " ", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</tr\s*>", "|\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<tr\b[^>]*>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</?(table|thead|tbody|tfoot)\b[^>]*>", "\n", RegexOptions.IgnoreCase);

            // Block breaks
            html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<hr\s*/?>", "\n---\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html,
                @"</?(p|div|section|article|main|aside|footer|blockquote|pre|figure|figcaption)\b[^>]*>",
                "\n",
                RegexOptions.IgnoreCase);

            // Drop remaining tags (keep their text)
            html = StripTags(html);
            html = HttpUtility.HtmlDecode(html);

            html = Regex.Replace(html, @"[ \t]+", " ");
            html = Regex.Replace(html, @" *\n *", "\n");
            html = Regex.Replace(html, @"\n{3,}", "\n\n");
            string body = html.Trim();

            return AssembleOutput(title, desc, body, links, maxContentLength);
        }

        private static string AssembleOutput(
            string title,
            string desc,
            string body,
            List<KeyValuePair<string, string>> links,
            int maxContentLength)
        {
            string header = "Title: " + (title ?? "") + "\nDesc: " + (desc ?? "") + "\n\n";
            List<KeyValuePair<string, string>> usedLinks = new List<KeyValuePair<string, string>>(links);
            string linksSection = FormatLinksSection(usedLinks);

            // 0 (or negative) = no length limit — keep full body and links.
            if (maxContentLength > 0)
            {
                while (header.Length + linksSection.Length > maxContentLength && usedLinks.Count > 0)
                {
                    usedLinks.RemoveAt(usedLinks.Count - 1);
                    linksSection = FormatLinksSection(usedLinks);
                }

                int bodyBudget = maxContentLength - header.Length - linksSection.Length;
                if (bodyBudget < 0)
                    bodyBudget = 0;

                if (body.Length > bodyBudget)
                    body = TruncateBody(body, bodyBudget);
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(header);
            sb.Append(body);
            sb.Append(linksSection);
            return sb.ToString();
        }

        private static string FormatLinksSection(List<KeyValuePair<string, string>> links)
        {
            if (links == null || links.Count == 0)
                return "";

            StringBuilder sb = new StringBuilder();
            sb.Append("\n\nLinks:");
            for (int i = 0; i < links.Count; i++)
            {
                sb.Append("\n- ");
                sb.Append(links[i].Key);
                sb.Append(": ");
                sb.Append(links[i].Value);
            }
            return sb.ToString();
        }

        private static string TruncateBody(string body, int maxLength)
        {
            // 0 (or negative) = no limit
            if (maxLength <= 0 || body.Length <= maxLength)
                return body;
            else
                return body.Substring(0, maxLength);
        }

        private static string ExtractMetaDescription(string html)
        {
            Match m = Regex.Match(html,
                @"<meta\b[^>]*\bname\s*=\s*(['""])description\1[^>]*\bcontent\s*=\s*(['""])(.*?)\2",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!m.Success)
            {
                m = Regex.Match(html,
                    @"<meta\b[^>]*\bcontent\s*=\s*(['""])(.*?)\1[^>]*\bname\s*=\s*(['""])description\3",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (m.Success)
                    return NormalizeWhitespace(HttpUtility.HtmlDecode(m.Groups[2].Value));
            }
            else
            {
                return NormalizeWhitespace(HttpUtility.HtmlDecode(m.Groups[3].Value));
            }

            m = Regex.Match(html,
                @"<meta\b[^>]*\bproperty\s*=\s*(['""])og:description\1[^>]*\bcontent\s*=\s*(['""])(.*?)\2",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (m.Success)
                return NormalizeWhitespace(HttpUtility.HtmlDecode(m.Groups[3].Value));

            m = Regex.Match(html,
                @"<meta\b[^>]*\bcontent\s*=\s*(['""])(.*?)\1[^>]*\bproperty\s*=\s*(['""])og:description\3",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (m.Success)
                return NormalizeWhitespace(HttpUtility.HtmlDecode(m.Groups[2].Value));

            return "";
        }

        private static string FormatImage(string attributes, Uri baseUri)
        {
            string alt = NormalizeWhitespace(HttpUtility.HtmlDecode(ExtractAttribute(attributes, "alt")));
            string src = ResolveUrl(baseUri, HttpUtility.HtmlDecode(ExtractAttribute(attributes, "src")));
            return FormatImageMarkup(alt, src);
        }

        /// <summary>
        /// Inline image markup shared by HTML and markdown paths. Drops data: URLs.
        /// </summary>
        private static string FormatImageMarkup(string alt, string src)
        {
            if (!string.IsNullOrEmpty(src) && src.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                src = ""; // data URLs are huge and not useful for download_file

            if (string.IsNullOrEmpty(alt) && string.IsNullOrEmpty(src))
                return "";
            if (string.IsNullOrEmpty(src))
                return "[Image: " + alt + "]";
            if (string.IsNullOrEmpty(alt))
                return "[Image](" + src + ")";
            return "[Image: " + alt + "](" + src + ")";
        }

        /// <summary>
        /// Adds a collectable, unique href to the Links list (up to maxLinks).
        /// Returns the label to leave in the body (empty if none).
        /// </summary>
        private static string CollectLinkBodyLabel(
            List<KeyValuePair<string, string>> links,
            HashSet<string> seenUrls,
            int maxLinks,
            string label,
            string href)
        {
            if (IsCollectableLink(href) && links.Count < maxLinks && seenUrls.Add(href))
            {
                if (string.IsNullOrEmpty(label))
                    label = href;
                links.Add(new KeyValuePair<string, string>(label, href));
            }

            return string.IsNullOrEmpty(label) ? "" : label;
        }

        private static string ExtractAttribute(string attributes, string name)
        {
            Match m = Regex.Match(attributes ?? "",
                @"\b" + name + @"\s*=\s*(['""])(.*?)\1",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return m.Success ? m.Groups[2].Value.Trim() : "";
        }

        private static string ResolveUrl(Uri baseUri, string href)
        {
            if (string.IsNullOrWhiteSpace(href))
                return "";

            if (baseUri != null)
            {
                Uri absolute;
                if (Uri.TryCreate(baseUri, href, out absolute))
                    return absolute.ToString();
            }

            return href;
        }

        private static bool IsCollectableLink(string href)
        {
            if (string.IsNullOrWhiteSpace(href))
                return false;

            string trimmed = href.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                return false;
            if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                return false;
            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        /// <summary>
        /// Removes doctype, comments, and common non-content blocks shared by readable and fallback paths.
        /// </summary>
        private static string StripNonContentBlocks(string html)
        {
            html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<html\b[^>]*>", "<html>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<script\b[^<]*(?:(?!</script>)<[^<]*)*</script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<style\b[^<]*(?:(?!</style>)<[^<]*)*</style>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<svg\b[^<]*(?:(?!</svg>)<[^<]*)*</svg>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<noscript\b[^<]*(?:(?!</noscript>)<[^<]*)*</noscript>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<nav\b[^<]*(?:(?!</nav>)<[^<]*)*</nav>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<header\b[^<]*(?:(?!</header>)<[^<]*)*</header>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<form\b[^<]*(?:(?!</form>)<[^<]*)*</form>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<head\b[^>]*>.*?</head>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return html;
        }

        /// <summary>
        /// Older HTML stripper used when readable-text conversion throws: remove fluff,
        /// simplify tags, keep &lt;img src/alt&gt; and &lt;a href&gt;.
        /// </summary>
        private static string FallbackPlainText(string html)
        {
            html = StripNonContentBlocks(html);
            html = Regex.Replace(html, @"<html\b[^>]*>", "<html>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<path\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<meta\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<link\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<input\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Keep img with src and alt only
            html = Regex.Replace(html,
                @"<img\b([^>]*)/?>",
                m =>
                {
                    string src = ExtractAttribute(m.Groups[1].Value, "src");
                    string alt = ExtractAttribute(m.Groups[1].Value, "alt");
                    if (string.IsNullOrEmpty(src) && string.IsNullOrEmpty(alt))
                        return "";
                    StringBuilder tag = new StringBuilder("<img");
                    if (!string.IsNullOrEmpty(src))
                        tag.Append(" src=\"").Append(src).Append("\"");
                    if (!string.IsNullOrEmpty(alt))
                        tag.Append(" alt=\"").Append(alt).Append("\"");
                    tag.Append(">");
                    return tag.ToString();
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Keep a tags with href only
            html = Regex.Replace(html,
                @"<a\b[^>]*\bhref\s*=\s*(['""])([^'""]*)\1[^>]*>",
                "<a href=\"$2\">",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Remove inline JS/CSS noise attributes
            html = Regex.Replace(html, @"\s(on\w+|style|class|id|method|role)\s*=\s*(['""]).*?\2", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"^\s*$[\r\n]*", "", RegexOptions.Multiline);
            html = Regex.Replace(html, @"</?html\b[^>]*>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</?body\b[^>]*>", "", RegexOptions.IgnoreCase);

            // Remove common wrappers but keep content
            html = Regex.Replace(html, @"</?[pibPIB]\b[^>]*>", "", RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?u\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?ul\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?ol\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?li\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            html = Regex.Replace(html, @"</?div\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?strong\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?span\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?pre\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?table\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?thead\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?tbody\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?tfoot\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?tr\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?td\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"</?th\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            html = Regex.Replace(html, @">\s+<", "><", RegexOptions.Singleline);
            html = Regex.Replace(html, @"[ \t]+", " ", RegexOptions.Multiline);
            html = Regex.Replace(html, @"^\s+|\s+$", "", RegexOptions.Multiline);

            return html.Trim();
        }

        private static string StripTags(string html)
        {
            return Regex.Replace(html, @"<[^>]+>", "");
        }

        private static string NormalizeWhitespace(string text)
        {
            return Regex.Replace(text ?? "", @"\s+", " ").Trim();
        }

        public static string RunWebSearch(string query, string searxngInstance,
            string firecrawlEndpoint, string firecrawlApiKey, int maxSearchResults, out int exitCode)
        {
            string output = "";
            exitCode = 0;

            // SearXNG > Firecrawl > DDG > Wiby
            if (!string.IsNullOrWhiteSpace(searxngInstance))
            {
                output = SearchHandler.RunSearXNGSearch(query, searxngInstance, maxSearchResults, out exitCode);
            }

            if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(firecrawlEndpoint))
            {
                output = SearchHandler.RunFirecrawlSearch(
                    query, firecrawlEndpoint, firecrawlApiKey, maxSearchResults, out exitCode);
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                output = SearchHandler.RunDDGSearch(query, maxSearchResults, out exitCode);
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                output = SearchHandler.RunWibySearch(query, maxSearchResults, out exitCode);
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                output = "No results found.";
            }

            return output;
        }
    }
}
