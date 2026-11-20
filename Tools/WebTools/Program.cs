using System;

namespace WebTools
{
    internal class Program
    {
        static int Main(string[] args)
        {
            return ToolHelper.RunToolMain(args, (toolName, argumentsJson) =>
            {
                switch (toolName)
                {
                    case "read_website":
                        {
                            string URL = ToolHelper.GetRequiredArg(argumentsJson, "URL");
                            int maxContentLength = ToolHelper.GetConfigInt("maxwebcontentlength", 10000);
                            int maxLinks = ToolHelper.GetConfigInt("maxlinks", 40);
                            string firecrawlEndpoint = ToolHelper.GetConfigValue("firecrawlendpoint");
                            string firecrawlApiKey = ToolHelper.GetConfigValue("firecrawlapikey");
                            return WebBrowser.ReadWebsite(
                                URL, maxContentLength, maxLinks, firecrawlEndpoint, firecrawlApiKey);
                        }

                    case "run_web_search":
                        {
                            string query = ToolHelper.GetRequiredArg(argumentsJson, "query");
                            string searxngInstance = ToolHelper.GetConfigValue("searxnginstance");
                            string firecrawlEndpoint = ToolHelper.GetConfigValue("firecrawlendpoint");
                            string firecrawlApiKey = ToolHelper.GetConfigValue("firecrawlapikey");
                            int maxSearchResults = ToolHelper.GetConfigInt("maxsearchresults", 20);
                            return new ToolResult(WebBrowser.RunWebSearch(
                                query, searxngInstance, firecrawlEndpoint, firecrawlApiKey, maxSearchResults), 0);
                        }

                    case "download_video":
                        {
                            string URL = ToolHelper.GetRequiredArg(argumentsJson, "URL");
                            return DownloadHandler.DownloadVideo(URL);
                        }

                    case "download_file":
                        {
                            string filename = ToolHelper.GetRequiredArg(argumentsJson, "filename");
                            string URL = ToolHelper.GetRequiredArg(argumentsJson, "URL");
                            return DownloadHandler.DownloadFile(filename, URL);
                        }

                    default:
                        return ToolHelper.Fail("unknown tool '" + toolName + "'.");
                }
            });
        }
    }
}
