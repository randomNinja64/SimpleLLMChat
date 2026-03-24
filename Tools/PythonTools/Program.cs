using Newtonsoft.Json.Linq;
using System;
using System.Text;

namespace PythonTools
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
                Console.Write("Usage: PythonTools.exe <tool_name>\nArguments JSON is read from stdin.");
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
                    case "run_python_script":
                        {
                            string scriptContent = ToolHelper.GetRequiredArg(argumentsJson, "script_content");
                            output = PythonRunner.RunPythonScript(scriptContent, out exitCode);
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
