using Newtonsoft.Json.Linq;
using System.IO;

namespace SkillTools.Tests
{
    internal static class Program
    {
        private const string Exe = "SkillTools.exe";

        static int Main(string[] args)
        {
            return SuiteMain.Run("SkillTools", args, Run);
        }

        static void Run()
        {
            TestRunner.Run("unknown_tool", () => ToolClient.AssertUnknownTool(Exe));

            using (TempWorkspace ws = new TempWorkspace("skilltools"))
            {
                JObject config = new JObject { ["skillsDirectory"] = ws.Path };

                TestRunner.Run("create_view_edit_file_remove", () =>
                {
                    ToolInvokeResult create = ToolClient.InvokeProduct(Exe, "create_skill",
                        new JObject
                        {
                            ["name"] = "demo",
                            ["description"] = "Demo skill",
                            ["instructions"] = "Do the demo."
                        }, config);
                    TestAssert.Equal(0, create.ExitCode, "create");

                    ToolInvokeResult view = ToolClient.InvokeProduct(Exe, "view_skill",
                        new JObject { ["name"] = "demo" }, config);
                    TestAssert.Equal(0, view.ExitCode, "view");
                    TestAssert.Contains(view.Text, "Do the demo.", "instructions");

                    ToolInvokeResult edit = ToolClient.InvokeProduct(Exe, "edit_skill",
                        new JObject
                        {
                            ["name"] = "demo",
                            ["description"] = "Updated",
                            ["instructions"] = "New instructions."
                        }, config);
                    TestAssert.Equal(0, edit.ExitCode, "edit");

                    ToolInvokeResult file = ToolClient.InvokeProduct(Exe, "edit_skill_file",
                        new JObject
                        {
                            ["name"] = "demo",
                            ["relative_path"] = "notes.txt",
                            ["content"] = "support file"
                        }, config);
                    TestAssert.Equal(0, file.ExitCode, "edit_skill_file");

                    ToolInvokeResult viewFile = ToolClient.InvokeProduct(Exe, "view_skill",
                        new JObject { ["name"] = "demo", ["relative_path"] = "notes.txt" }, config);
                    TestAssert.Equal(0, viewFile.ExitCode, "view file");
                    TestAssert.Contains(viewFile.Text, "support file", "file content");

                    ToolInvokeResult ctx = ToolClient.InvokeProduct(Exe, "get_skills_context",
                        new JObject(), config);
                    TestAssert.Equal(0, ctx.ExitCode, "context");
                    TestAssert.ContainsIgnoreCase(ctx.Text, "demo", "context lists skill");

                    ToolInvokeResult remove = ToolClient.InvokeProduct(Exe, "remove_skill",
                        new JObject { ["name"] = "demo" }, config);
                    TestAssert.Equal(0, remove.ExitCode, "remove");
                });
            }
        }
    }
}
