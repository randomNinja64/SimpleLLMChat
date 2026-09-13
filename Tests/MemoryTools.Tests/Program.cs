using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace MemoryTools.Tests
{
    internal static class Program
    {
        private const string Exe = "MemoryTools.exe";

        static int Main(string[] args)
        {
            return SuiteMain.Run("MemoryTools", args, Run);
        }

        static void Run()
        {
            TestRunner.Run("unknown_tool", () => ToolClient.AssertUnknownTool(Exe));

            using (TempWorkspace ws = new TempWorkspace("memorytools"))
            {
                JObject config = new JObject { ["memoriesDirectory"] = ws.Path };

                TestRunner.Run("save_recall_list_search_delete", () =>
                {
                    ToolInvokeResult save = ToolClient.InvokeProduct(Exe, "save_memory",
                        new JObject { ["name"] = "note1", ["content"] = "alpha beta gamma" }, config);
                    TestAssert.Equal(0, save.ExitCode, "save");

                    ToolInvokeResult recall = ToolClient.InvokeProduct(Exe, "recall_memory",
                        new JObject { ["name"] = "note1" }, config);
                    TestAssert.Equal(0, recall.ExitCode, "recall");
                    TestAssert.Contains(recall.Text, "alpha beta gamma", "recall content");

                    ToolInvokeResult list = ToolClient.InvokeProduct(Exe, "list_memories",
                        new JObject(), config);
                    TestAssert.Equal(0, list.ExitCode, "list");
                    TestAssert.Contains(list.Text, "note1", "list");

                    ToolInvokeResult search = ToolClient.InvokeProduct(Exe, "search_memories",
                        new JObject { ["keyword"] = "beta" }, config);
                    TestAssert.Equal(0, search.ExitCode, "search");
                    TestAssert.ContainsIgnoreCase(search.Text, "note1", "search hit");

                    ToolInvokeResult ctx = ToolClient.InvokeProduct(Exe, "get_memory_context",
                        new JObject(), config);
                    TestAssert.Equal(0, ctx.ExitCode, "context");

                    ToolInvokeResult del = ToolClient.InvokeProduct(Exe, "delete_memory",
                        new JObject { ["name"] = "note1" }, config);
                    TestAssert.Equal(0, del.ExitCode, "delete");
                });

                TestRunner.Run("missing_name", () =>
                {
                    try
                    {
                        ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "save_memory",
                            new JObject { ["content"] = "x" }, config);
                        TestAssert.True(r.ExitCode != 0, "missing name should fail");
                        TestAssert.ContainsIgnoreCase(r.Text ?? r.Stdout, "missing", "error text");
                    }
                    catch (Exception)
                    {
                        // ToolHelper may return error JSON with exit 1
                        throw;
                    }
                });
            }
        }
    }
}
