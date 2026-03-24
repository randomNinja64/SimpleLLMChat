using Newtonsoft.Json.Linq;
using System;
using System.Text;

namespace WebTools
{
    internal class Program
    {
        static int Main(string[] args)
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
            ToolHelper.LoadManifestDefaults();

            if (args.Length < 1)
            {
                Console.Write("Usage: WebTools.exe <tool_name>\nArguments JSON is read from stdin.");
                return 1;
            }

            string toolName = args[0];

            // Read arguments JSON from stdin
            string stdinData = Console.In.ReadToEnd();
            string argumentsJson = "";

            if (!string.IsNullOrWhiteSpace(stdinData))
            {
                try
                {
                    JObject root = JObject.Parse(stdinData);

                    // Extract config if present
                    JObject configObj = root["config"] as JObject;
                    if (configObj != null)
                    {
                        foreach (JProperty prop in configObj.Properties())
                        {
                            ToolHelper.Config[prop.Name] = prop.Value.ToString();
                        }
                    }

                    // Extract arguments if present
                    JToken argsToken = root["arguments"];
                    if (argsToken != null)
                    {
                        argumentsJson = argsToken.ToString();
                    }
                }
                catch
                {
                    // If parsing fails, treat whole stdin as arguments JSON
                    argumentsJson = stdinData;
                }
            }

            int exitCode = 0;
            string output = "";

            try
            {
                switch (toolName)
                {
                    case "read_website":
                        {
                            string URL = ToolHelper.GetRequiredArg(argumentsJson, "URL");
                            int maxContentLength = ToolHelper.GetConfigInt("maxwebcontentlength", 8000);
                            output = WebBrowser.ReadWebsite(URL, maxContentLength, out exitCode);
                            break;
                        }

                    case "run_web_search":
                        {
                            string query = ToolHelper.GetRequiredArg(argumentsJson, "query");
                            string searxngInstance = ToolHelper.GetConfigString("searxnginstance");
                            int maxSearchResults = ToolHelper.GetConfigInt("maxsearchresults", 20);
                            output = WebBrowser.RunWebSearch(query, searxngInstance, maxSearchResults, out exitCode);
                            break;
                        }

                    case "download_video":
                        {
                            string URL = ToolHelper.GetRequiredArg(argumentsJson, "URL");
                            output = DownloadHandler.DownloadVideo(URL, out exitCode);
                            break;
                        }

                    case "download_file":
                        {
                            string filename = ToolHelper.GetRequiredArg(argumentsJson, "filename");
                            string URL = ToolHelper.GetRequiredArg(argumentsJson, "URL");
                            output = DownloadHandler.DownloadFile(filename, URL, out exitCode);
                            break;
                        }

                    default:
                        output = "error: unknown tool '" + toolName + "'.";
                        exitCode = 1;
                        break;
                }
            }
            catch (Exception e)
            {
                output = "error: " + e.Message;
                exitCode = 1;
            }

            Console.Write(output);
            return exitCode;
        }
    }
}
