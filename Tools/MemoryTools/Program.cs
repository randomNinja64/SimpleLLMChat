using System;

namespace MemoryTools
{
    internal class Program
    {
        static int Main(string[] args)
        {
            return ToolHelper.RunToolMain(args, (toolName, argumentsJson) =>
            {
                switch (toolName)
                {
                    case "save_memory":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            string content = ToolHelper.GetRequiredArg(argumentsJson, "content");
                            return new ToolResult(MemoryHandler.SaveMemory(name, content), 0);
                        }

                    case "recall_memory":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            return new ToolResult(MemoryHandler.RecallMemory(name), 0);
                        }

                    case "delete_memory":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            return new ToolResult(MemoryHandler.DeleteMemory(name), 0);
                        }

                    case "list_memories":
                        return new ToolResult(MemoryHandler.ListMemories(), 0);

                    case "search_memories":
                        {
                            string keyword = ToolHelper.GetRequiredArg(argumentsJson, "keyword");
                            return new ToolResult(MemoryHandler.SearchMemories(keyword), 0);
                        }

                    case "get_memory_context":
                        return new ToolResult(MemoryHandler.GetContext() ?? "", 0);

                    default:
                        return new ToolResult("error: unknown tool '" + toolName + "'.", 1);
                }
            });
        }
    }
}
