using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;

namespace MemoryTools
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
                Console.Write("Usage: MemoryTools.exe <tool_name>\nArguments JSON is read from stdin.");
                return 1;
            }

            string toolName = args[0];

            string stdinData = Console.In.ReadToEnd();
            string argumentsJson = "";

            if (!string.IsNullOrWhiteSpace(stdinData))
            {
                try
                {
                    JObject root = JObject.Parse(stdinData);

                    JObject configObj = root["config"] as JObject;
                    if (configObj != null)
                    {
                        foreach (JProperty prop in configObj.Properties())
                            ToolHelper.Config[prop.Name] = prop.Value.ToString();
                    }

                    JToken argsToken = root["arguments"];
                    if (argsToken != null)
                        argumentsJson = argsToken.ToString();
                }
                catch
                {
                    argumentsJson = stdinData;
                }
            }

            MemoryHandler handler = new MemoryHandler();

            int exitCode = 0;
            string output = "";

            try
            {
                switch (toolName)
                {
                    case "save_memory":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            string content = ToolHelper.GetRequiredArg(argumentsJson, "content");
                            output = handler.SaveMemory(name, content);
                            break;
                        }

                    case "recall_memory":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            output = handler.RecallMemory(name);
                            break;
                        }

                    case "delete_memory":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            output = handler.DeleteMemory(name);
                            break;
                        }

                    case "list_memories":
                        {
                            output = handler.ListMemories();
                            break;
                        }

                    case "search_memories":
                        {
                            string keyword = ToolHelper.GetRequiredArg(argumentsJson, "keyword");
                            output = handler.SearchMemories(keyword);
                            break;
                        }

                    default:
                        exitCode = 1;
                        output = "Unknown tool: " + toolName;
                        break;
                }
            }
            catch (ArgumentException ex)
            {
                exitCode = 1;
                output = "Error: " + ex.Message;
            }
            catch (Exception ex)
            {
                exitCode = 1;
                output = "Unexpected error: " + ex.Message;
            }

            Console.Write(output);
            return exitCode;
        }
    }
}
