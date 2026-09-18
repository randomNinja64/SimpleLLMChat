using Newtonsoft.Json.Linq;
using System;
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

                TestRunner.Run("view_missing", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "view_skill",
                        new JObject { ["name"] = "nope" }, config);
                    ToolClient.AssertToolError(r, "does not exist");
                    TestAssert.Equal(1, r.ExitCode, "exit 1");
                });

                TestRunner.Run("create_duplicate", () =>
                {
                    string dir = Path.Combine(ws.Path, "dup");
                    Directory.CreateDirectory(dir);
                    JObject cfg = new JObject { ["skillsDirectory"] = dir };
                    ToolInvokeResult first = ToolClient.InvokeProduct(Exe, "create_skill",
                        new JObject
                        {
                            ["name"] = "dup-skill",
                            ["description"] = "once",
                            ["instructions"] = "body"
                        }, cfg);
                    TestAssert.Equal(0, first.ExitCode, "first");
                    ToolInvokeResult second = ToolClient.InvokeProduct(Exe, "create_skill",
                        new JObject
                        {
                            ["name"] = "dup-skill",
                            ["description"] = "twice",
                            ["instructions"] = "body"
                        }, cfg);
                    ToolClient.AssertToolError(second, "already exists");
                });

                TestRunner.Run("create_invalid_name", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "create_skill",
                        new JObject
                        {
                            ["name"] = "CodeReview",
                            ["description"] = "x",
                            ["instructions"] = "y"
                        }, config);
                    ToolClient.AssertToolError(r, "lowercase");
                });

                TestRunner.Run("create_name_too_long", () =>
                {
                    string longName = new string('a', 65);
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "create_skill",
                        new JObject
                        {
                            ["name"] = longName,
                            ["description"] = "x",
                            ["instructions"] = "y"
                        }, config);
                    ToolClient.AssertToolError(r, "64 characters or fewer");
                });

                TestRunner.Run("create_description_truncated", () =>
                {
                    string dir = Path.Combine(ws.Path, "trunc");
                    Directory.CreateDirectory(dir);
                    JObject cfg = new JObject { ["skillsDirectory"] = dir };
                    string longDesc = new string('d', 1100);
                    ToolInvokeResult create = ToolClient.InvokeProduct(Exe, "create_skill",
                        new JObject
                        {
                            ["name"] = "long-desc",
                            ["description"] = longDesc,
                            ["instructions"] = "body"
                        }, cfg);
                    TestAssert.Equal(0, create.ExitCode, "create");
                    TestAssert.Contains(create.Text, "description truncated to 1024", "trunc note");

                    ToolInvokeResult view = ToolClient.InvokeProduct(Exe, "view_skill",
                        new JObject { ["name"] = "long-desc" }, cfg);
                    TestAssert.Equal(0, view.ExitCode, "view");
                    TestAssert.Contains(view.Text, "...truncated", "marker");
                    TestAssert.True(view.Text.IndexOf(longDesc, StringComparison.Ordinal) < 0, "full desc absent");

                    ToolInvokeResult ctx = ToolClient.InvokeProduct(Exe, "get_skills_context",
                        new JObject(), cfg);
                    TestAssert.Equal(0, ctx.ExitCode, "context");
                    TestAssert.Contains(ctx.Text, "...truncated", "context trunc");
                });

                TestRunner.Run("edit_missing", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "edit_skill",
                        new JObject
                        {
                            ["name"] = "missing-skill",
                            ["description"] = "x"
                        }, config);
                    ToolClient.AssertToolError(r, "does not exist");
                });

                TestRunner.Run("remove_missing", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "remove_skill",
                        new JObject { ["name"] = "missing-skill" }, config);
                    ToolClient.AssertToolError(r, "does not exist");
                });

                TestRunner.Run("edit_skill_file_path_guards", () =>
                {
                    string dir = Path.Combine(ws.Path, "paths");
                    Directory.CreateDirectory(dir);
                    JObject cfg = new JObject { ["skillsDirectory"] = dir };
                    ToolInvokeResult create = ToolClient.InvokeProduct(Exe, "create_skill",
                        new JObject
                        {
                            ["name"] = "path-skill",
                            ["description"] = "paths",
                            ["instructions"] = "body"
                        }, cfg);
                    TestAssert.Equal(0, create.ExitCode, "create");

                    ToolInvokeResult dotdot = ToolClient.InvokeProduct(Exe, "edit_skill_file",
                        new JObject
                        {
                            ["name"] = "path-skill",
                            ["relative_path"] = "../escape.txt",
                            ["content"] = "nope"
                        }, cfg);
                    ToolClient.AssertToolError(dotdot, "no '..' segments");

                    ToolInvokeResult abs = ToolClient.InvokeProduct(Exe, "edit_skill_file",
                        new JObject
                        {
                            ["name"] = "path-skill",
                            ["relative_path"] = @"C:\Windows\Temp\escape.txt",
                            ["content"] = "nope"
                        }, cfg);
                    ToolClient.AssertToolError(abs, "no absolute paths");

                    ToolInvokeResult skillMd = ToolClient.InvokeProduct(Exe, "edit_skill_file",
                        new JObject
                        {
                            ["name"] = "path-skill",
                            ["relative_path"] = "SKILL.md",
                            ["content"] = "nope"
                        }, cfg);
                    ToolClient.AssertToolError(skillMd, "cannot modify SKILL.md");
                });

                TestRunner.Run("get_skills_context_empty", () =>
                {
                    string dir = Path.Combine(ws.Path, "empty-skills");
                    Directory.CreateDirectory(dir);
                    JObject cfg = new JObject { ["skillsDirectory"] = dir };
                    ToolInvokeResult ctx = ToolClient.InvokeProduct(Exe, "get_skills_context",
                        new JObject(), cfg);
                    TestAssert.Equal(0, ctx.ExitCode, "exit");
                    TestAssert.Contains(ctx.Text, "(no skills installed)", "empty");
                });
            }
        }
    }
}
