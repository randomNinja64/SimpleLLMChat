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
                            int maxContentLength = ToolHelper.GetConfigInt("maxwebcontentlength", 8000);
                            int exitCode;
                            string output = WebBrowser.ReadWebsite(URL, maxContentLength, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    case "run_web_search":
                        {
                            string query = ToolHelper.GetRequiredArg(argumentsJson, "query");
                            string searxngInstance = ToolHelper.GetConfigString("searxnginstance");
                            int maxSearchResults = ToolHelper.GetConfigInt("maxsearchresults", 20);
                            int exitCode;
                            string output = WebBrowser.RunWebSearch(query, searxngInstance, maxSearchResults, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    case "download_video":
                        {
                            string URL = ToolHelper.GetRequiredArg(argumentsJson, "URL");
                            int exitCode;
                            string output = DownloadHandler.DownloadVideo(URL, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    case "download_file":
                        {
                            string filename = ToolHelper.GetRequiredArg(argumentsJson, "filename");
                            string URL = ToolHelper.GetRequiredArg(argumentsJson, "URL");
                            int exitCode;
                            string output = DownloadHandler.DownloadFile(filename, URL, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    default:
                        return new ToolResult("error: unknown tool '" + toolName + "'.", 1);
                }
            });
        }
    }
}
