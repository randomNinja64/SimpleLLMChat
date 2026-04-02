using System;

namespace MemoryTools
{
    internal class Program
    {
        static int Main(string[] args)
        {
            return ToolHelper.RunToolMain(args, (toolName, argumentsJson) =>
            {
                MemoryHandler handler = new MemoryHandler();

                switch (toolName)
                {
                    case "save_memory":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            string content = ToolHelper.GetRequiredArg(argumentsJson, "content");
                            return new ToolResult(handler.SaveMemory(name, content), 0);
                        }

                    case "recall_memory":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            return new ToolResult(handler.RecallMemory(name), 0);
                        }

                    case "delete_memory":
                        {
                            string name = ToolHelper.GetRequiredArg(argumentsJson, "name");
                            return new ToolResult(handler.DeleteMemory(name), 0);
                        }

                    case "list_memories":
                        return new ToolResult(handler.ListMemories(), 0);

                    case "search_memories":
                        {
                            string keyword = ToolHelper.GetRequiredArg(argumentsJson, "keyword");
                            return new ToolResult(handler.SearchMemories(keyword), 0);
                        }

                    default:
                        return new ToolResult("Unknown tool: " + toolName, 1);
                }
            });
        }
    }
}
