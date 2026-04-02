using System;

namespace ShellTools
{
    internal class Program
    {
        static int Main(string[] args)
        {
            return ToolHelper.RunToolMain(args, (toolName, argumentsJson) =>
            {
                switch (toolName)
                {
                    case "run_shell_command":
                        {
                            string command = ToolHelper.GetRequiredArg(argumentsJson, "command");
                            int exitCode;
                            string output = ToolHelper.ExecuteProcess("cmd.exe", "/c " + command, out exitCode);
                            return new ToolResult(output, exitCode);
                        }

                    default:
                        return new ToolResult("error: unknown tool '" + toolName + "'.", 1);
                }
            });
        }
    }
}
