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
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "save_memory",
                        new JObject { ["content"] = "x" }, config);
                    ToolClient.AssertToolError(r, "missing");
                });

                TestRunner.Run("recall_missing", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "recall_memory",
                        new JObject { ["name"] = "does-not-exist" }, config);
                    ToolClient.AssertToolError(r, "no memory entry");
                    TestAssert.Equal(1, r.ExitCode, "exit 1");
                });

                TestRunner.Run("delete_missing", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "delete_memory",
                        new JObject { ["name"] = "does-not-exist" }, config);
                    ToolClient.AssertToolError(r, "no memory entry");
                    TestAssert.Equal(1, r.ExitCode, "exit 1");
                });

                TestRunner.Run("save_truncates_content", () =>
                {
                    string dir = Path.Combine(ws.Path, "trunc");
                    Directory.CreateDirectory(dir);
                    JObject cfg = new JObject
                    {
                        ["memoriesDirectory"] = dir,
                        ["maxContentLength"] = "10"
                    };
                    ToolInvokeResult save = ToolClient.InvokeProduct(Exe, "save_memory",
                        new JObject { ["name"] = "long", ["content"] = "abcdefghijklmnop" }, cfg);
                    TestAssert.Equal(0, save.ExitCode, "save");
                    TestAssert.Contains(save.Text, "content truncated to 10", "trunc note");

                    ToolInvokeResult recall = ToolClient.InvokeProduct(Exe, "recall_memory",
                        new JObject { ["name"] = "long" }, cfg);
                    TestAssert.Equal(0, recall.ExitCode, "recall");
                    TestAssert.Equal(10, recall.Text.Length, "recall length");
                    TestAssert.Equal("abcdefghij", recall.Text, "recall body");
                });

                TestRunner.Run("save_evicts_oldest_at_cap", () =>
                {
                    string dir = Path.Combine(ws.Path, "evict");
                    Directory.CreateDirectory(dir);
                    JObject cfg = new JObject
                    {
                        ["memoriesDirectory"] = dir,
                        ["maxMemories"] = "3"
                    };
                    DateTime baseUtc = DateTime.UtcNow.AddMinutes(-10);
                    string[] names = { "A", "B", "C" };
                    for (int i = 0; i < names.Length; i++)
                    {
                        ToolInvokeResult s = ToolClient.InvokeProduct(Exe, "save_memory",
                            new JObject { ["name"] = names[i], ["content"] = "body-" + names[i] }, cfg);
                        TestAssert.Equal(0, s.ExitCode, "save " + names[i]);
                        File.SetLastWriteTimeUtc(Path.Combine(dir, names[i] + ".md"), baseUtc.AddMinutes(i));
                    }

                    ToolInvokeResult saveD = ToolClient.InvokeProduct(Exe, "save_memory",
                        new JObject { ["name"] = "D", ["content"] = "body-D" }, cfg);
                    TestAssert.Equal(0, saveD.ExitCode, "save D");
                    TestAssert.ContainsIgnoreCase(saveD.Text, "evicted oldest: A", "eviction note");

                    ToolInvokeResult list = ToolClient.InvokeProduct(Exe, "list_memories",
                        new JObject(), cfg);
                    TestAssert.Equal(0, list.ExitCode, "list");
                    TestAssert.Contains(list.Text, "B", "has B");
                    TestAssert.Contains(list.Text, "C", "has C");
                    TestAssert.Contains(list.Text, "D", "has D");
                    TestAssert.True(!File.Exists(Path.Combine(dir, "A.md")), "A file gone");
                });

                TestRunner.Run("save_update_does_not_evict", () =>
                {
                    string dir = Path.Combine(ws.Path, "update");
                    Directory.CreateDirectory(dir);
                    JObject cfg = new JObject
                    {
                        ["memoriesDirectory"] = dir,
                        ["maxMemories"] = "2"
                    };
                    ToolClient.InvokeProduct(Exe, "save_memory",
                        new JObject { ["name"] = "keep1", ["content"] = "one" }, cfg);
                    ToolClient.InvokeProduct(Exe, "save_memory",
                        new JObject { ["name"] = "keep2", ["content"] = "two" }, cfg);

                    ToolInvokeResult upd = ToolClient.InvokeProduct(Exe, "save_memory",
                        new JObject { ["name"] = "keep1", ["content"] = "one-updated" }, cfg);
                    TestAssert.Equal(0, upd.ExitCode, "update");
                    TestAssert.True(
                        upd.Text.IndexOf("evicted", StringComparison.OrdinalIgnoreCase) < 0,
                        "no eviction");

                    ToolInvokeResult list = ToolClient.InvokeProduct(Exe, "list_memories",
                        new JObject(), cfg);
                    TestAssert.Contains(list.Text, "keep1", "keep1");
                    TestAssert.Contains(list.Text, "keep2", "keep2");
                    TestAssert.Equal(2, Directory.GetFiles(dir, "*.md").Length, "count");
                });

                TestRunner.Run("save_missing_content", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "save_memory",
                        new JObject { ["name"] = "n" }, config);
                    ToolClient.AssertToolError(r, "missing");
                });

                TestRunner.Run("save_name_too_long", () =>
                {
                    string longName = new string('n', 101);
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "save_memory",
                        new JObject { ["name"] = longName, ["content"] = "x" }, config);
                    ToolClient.AssertToolError(r, "100 characters or fewer");
                });

                TestRunner.Run("search_no_match", () =>
                {
                    string dir = Path.Combine(ws.Path, "search");
                    Directory.CreateDirectory(dir);
                    JObject cfg = new JObject { ["memoriesDirectory"] = dir };
                    ToolClient.InvokeProduct(Exe, "save_memory",
                        new JObject { ["name"] = "only", ["content"] = "hello world" }, cfg);
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "search_memories",
                        new JObject { ["keyword"] = "NOMATCH_XYZ" }, cfg);
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Contains(r.Text, "No memories matched", "no match");
                });
            }
        }
    }
}
