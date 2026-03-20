using System;
using System.Text.RegularExpressions;

namespace BuiltInTools
{
    public static class WebTools
    {
        public static string ReadWebsite(string URL, int maxContentLength, out int exitCode)
        {
            string html = "";

            try
            {
                // Build curl command arguments
                string arguments = "-s -L \"" + URL + "\" " +
                                   "-H \"User-Agent: " + ToolHelper.USER_AGENT + "\"";

                html = ToolHelper.ExecuteProcess("curl.exe", arguments, out exitCode, combineErrorOutput: false);

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
                if (html.Length > maxContentLength)
                    html = html.Substring(0, maxContentLength);
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "Error running curl.exe: " + ex.Message;
            }

            return html + "\n";
        }

        public static string RunWebSearch(string query, string searxngInstance, int maxSearchResults, out int exitCode)
        {
            string output = "";
            exitCode = 0;

            // If SearXNG instance is set, try it first
            if (!string.IsNullOrWhiteSpace(searxngInstance))
            {
                output = SearchHandler.RunSearXNGSearch(query, searxngInstance, maxSearchResults, out exitCode);
            }

            // If no results yet, try DDG
            if (string.IsNullOrWhiteSpace(output) || output.Trim() == "")
            {
                output = SearchHandler.RunDDGSearch(query, maxSearchResults, out exitCode);
            }

            // If no results yet, try Wiby
            if (string.IsNullOrWhiteSpace(output) || output.Trim() == "")
            {
                output = SearchHandler.RunWibySearch(query, maxSearchResults, out exitCode);
            }

            // If no results from all 3, set output to "No results found."
            if (string.IsNullOrWhiteSpace(output) || output.Trim() == "")
            {
                output = "No results found.";
            }

            return output;
        }
    }
}
