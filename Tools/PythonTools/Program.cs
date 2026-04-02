using System;

namespace PythonTools
{
    internal class Program
    {
        static int Main(string[] args)
        {
            return ToolHelper.RunToolMain(args, (toolName, argumentsJson) =>
            {
                switch (toolName)
                {
                    case "run_python_script":
                        {
                            string scriptContent = ToolHelper.GetRequiredArg(argumentsJson, "script_content");
                            int exitCode;
                            string output = PythonRunner.RunPythonScript(scriptContent, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    default:
                        return new ToolResult("error: unknown tool '" + toolName + "'.", 1);
                }
            });
        }
    }
}
