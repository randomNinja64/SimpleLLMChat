using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;

namespace FileTools.Tests
{
    internal static class Program
    {
        private const string Exe = "FileTools.exe";

        static int Main(string[] args)
        {
            return SuiteMain.Run("FileTools", args, Run);
        }

        static void Run()
        {
            TestRunner.Run("unknown_tool", () => ToolClient.AssertUnknownTool(Exe));

            using (TempWorkspace ws = new TempWorkspace("filetools"))
            {
                string hello = ws.Combine("hello.txt");
                File.WriteAllText(hello, "hello world", Encoding.UTF8);

                TestRunner.Run("read_file.basic", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "read_file",
                        new JObject { ["filename"] = hello });
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Contains(r.Text, "hello world", "content");
                });

                TestRunner.Run("write_file.basic", () =>
                {
                    string path = ws.Combine("written.txt");
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "write_file",
                        new JObject { ["filename"] = path, ["content"] = "abc" });
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Equal("abc", File.ReadAllText(path, Encoding.UTF8), "disk");
                });

                TestRunner.Run("edit_file.replace", () =>
                {
                    string path = ws.Combine("edit.txt");
                    File.WriteAllText(path, "one two three", Encoding.UTF8);
                    JArray edits = new JArray
                    {
                        new JObject { ["old_string"] = "two", ["new_string"] = "2" }
                    };
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "edit_file",
                        new JObject { ["filename"] = path, ["edits"] = edits });
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Equal("one 2 three", File.ReadAllText(path, Encoding.UTF8), "disk");
                });

                TestRunner.Run("copy_move_delete_list", () =>
                {
                    string src = ws.Combine("src.txt");
                    string copy = ws.Combine("copy.txt");
                    string moved = ws.Combine("moved.txt");
                    File.WriteAllText(src, "payload", Encoding.UTF8);

                    ToolInvokeResult c = ToolClient.InvokeProduct(Exe, "copy_file",
                        new JObject { ["source_path"] = src, ["destination_path"] = copy });
                    TestAssert.Equal(0, c.ExitCode, "copy exit");
                    TestAssert.True(File.Exists(copy), "copy exists");

                    ToolInvokeResult m = ToolClient.InvokeProduct(Exe, "move_file",
                        new JObject { ["source_path"] = copy, ["destination_path"] = moved });
                    TestAssert.Equal(0, m.ExitCode, "move exit");
                    TestAssert.True(File.Exists(moved), "moved exists");
                    TestAssert.True(!File.Exists(copy), "copy gone");

                    ToolInvokeResult list = ToolClient.InvokeProduct(Exe, "list_directory",
                        new JObject { ["directory_path"] = ws.Path });
                    TestAssert.Equal(0, list.ExitCode, "list exit");
                    TestAssert.Contains(list.Text, "moved.txt", "list contains moved");

                    ToolInvokeResult d = ToolClient.InvokeProduct(Exe, "delete_file",
                        new JObject { ["file_path"] = moved });
                    TestAssert.Equal(0, d.ExitCode, "delete exit");
                    TestAssert.True(!File.Exists(moved), "deleted");
                });

                TestRunner.Run("read_file.missing", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "read_file",
                        new JObject { ["filename"] = ws.Combine("nope.txt") });
                    ToolClient.AssertToolError(r, "not found");
                    TestAssert.Equal(1, r.ExitCode, "exit 1");
                });

                TestRunner.Run("extract_file", () =>
                {
                    if (!ToolClient.ProductExists("7za.exe"))
                        TestRunner.Skip("7za.exe not packaged beside test EXE");

                    // Create a tiny zip via PowerShell is heavy; skip creating archive if 7za missing already handled.
                    // Use 7za to create then extract.
                    string archive = ws.Combine("a.zip");
                    string member = ws.Combine("member.txt");
                    File.WriteAllText(member, "zip-body", Encoding.UTF8);
                    ProcessResult pack = ProcessRunner.Run(
                        ToolClient.ProductExe("7za.exe"),
                        "a \"" + archive + "\" \"" + member + "\"",
                        ws.Path, null, 30000);
                    TestAssert.Equal(0, pack.ExitCode, "7za pack");

                    string dest = ws.Combine("extracted");
                    Directory.CreateDirectory(dest);
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "extract_file",
                        new JObject { ["archive_path"] = archive, ["destination_path"] = dest });
                    TestAssert.Equal(0, r.ExitCode, "extract exit");
                });
            }
        }
    }
}
