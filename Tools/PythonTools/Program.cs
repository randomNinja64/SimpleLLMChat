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
                            return PythonRunner.RunPythonScript(scriptContent);
                        }

                    default:
                        return ToolHelper.Fail("unknown tool '" + toolName + "'.");
                }
            });
        }
    }
}
