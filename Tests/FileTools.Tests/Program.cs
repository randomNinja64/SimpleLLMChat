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

                TestRunner.Run("read_file.offset_and_truncate", () =>
                {
                    string path = ws.Combine("long.txt");
                    File.WriteAllText(path, "0123456789ABCDEF", Encoding.UTF8);
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "read_file",
                        new JObject { ["filename"] = path, ["offset"] = "0" },
                        new JObject { ["maxFileContentLength"] = "10" });
                    TestAssert.Equal(0, r.ExitCode, "exit");
                    TestAssert.Contains(r.Text, "reading chars 0-9", "header");
                    TestAssert.Contains(r.Text, "...[truncated]", "trunc");
                    TestAssert.Contains(r.Text, "0123456789", "body");
                    TestAssert.True(r.Text.IndexOf("ABCDEF", StringComparison.Ordinal) < 0, "no tail");
                });

                TestRunner.Run("read_file.offset_past_eof", () =>
                {
                    string path = ws.Combine("short.txt");
                    File.WriteAllText(path, "abc", Encoding.UTF8);
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "read_file",
                        new JObject { ["filename"] = path, ["offset"] = "3" });
                    ToolClient.AssertToolError(r, "exceeds file length");
                    TestAssert.Equal(1, r.ExitCode, "exit 1");
                });

                TestRunner.Run("read_file.env_temp", () =>
                {
                    string folder = Path.Combine(Path.GetTempPath(), "filetools-env-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(folder);
                    try
                    {
                        string leaf = "env-hello.txt";
                        File.WriteAllText(Path.Combine(folder, leaf), "from-temp", Encoding.UTF8);
                        string envPath = "%TEMP%\\" + Path.GetFileName(folder) + "\\" + leaf;
                        ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "read_file",
                            new JObject { ["filename"] = envPath });
                        TestAssert.Equal(0, r.ExitCode, "exit");
                        TestAssert.Contains(r.Text, "from-temp", "content");
                    }
                    finally
                    {
                        try { Directory.Delete(folder, true); } catch { }
                    }
                });

                TestRunner.Run("copy_file.dest_exists", () =>
                {
                    string src = ws.Combine("copy-src.txt");
                    string dest = ws.Combine("copy-dest.txt");
                    File.WriteAllText(src, "src", Encoding.UTF8);
                    File.WriteAllText(dest, "dest", Encoding.UTF8);
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "copy_file",
                        new JObject { ["source_path"] = src, ["destination_path"] = dest });
                    ToolClient.AssertToolError(r, "already exists");
                });

                TestRunner.Run("edit_file.old_string_ambiguous", () =>
                {
                    string path = ws.Combine("ambig.txt");
                    File.WriteAllText(path, "xx mid xx", Encoding.UTF8);
                    JArray edits = new JArray
                    {
                        new JObject { ["old_string"] = "xx", ["new_string"] = "YY" }
                    };
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "edit_file",
                        new JObject { ["filename"] = path, ["edits"] = edits });
                    ToolClient.AssertToolError(r, "appears");
                    TestAssert.Equal("xx mid xx", File.ReadAllText(path, Encoding.UTF8), "unchanged");
                });

                TestRunner.Run("delete_file.missing", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "delete_file",
                        new JObject { ["file_path"] = ws.Combine("no-delete.txt") });
                    ToolClient.AssertToolError(r, "not found");
                });

                TestRunner.Run("list_directory.missing", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "list_directory",
                        new JObject { ["directory_path"] = ws.Combine("no-such-dir") });
                    ToolClient.AssertToolError(r, "Directory not found");
                });

                TestRunner.Run("extract_file.archive_missing", () =>
                {
                    ToolInvokeResult r = ToolClient.InvokeProduct(Exe, "extract_file",
                        new JObject
                        {
                            ["archive_path"] = ws.Combine("missing.zip"),
                            ["destination_path"] = ws.Combine("extract-out")
                        });
                    ToolClient.AssertToolError(r, "Archive not found");
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

                TestRunner.Run("grep_search", () =>
                {
                    if (!ToolClient.ProductExists("grep.exe"))
                        TestRunner.Skip("grep.exe not packaged beside test EXE");

                    string hit = ws.Combine("hit.txt");
                    string miss = ws.Combine("miss.txt");
                    File.WriteAllText(hit, "alpha FINDME omega", Encoding.UTF8);
                    File.WriteAllText(miss, "nothing here", Encoding.UTF8);

                    string noiseDir = Path.Combine(ws.Path, "node_modules");
                    Directory.CreateDirectory(noiseDir);
                    File.WriteAllText(Path.Combine(noiseDir, "secret.txt"), "FINDME in node_modules", Encoding.UTF8);

                    string csOnly = ws.Combine("code.cs");
                    File.WriteAllText(csOnly, "FINDME in csharp", Encoding.UTF8);

                    ToolInvokeResult hitResult = ToolClient.InvokeProduct(Exe, "grep_search",
                        new JObject
                        {
                            ["pattern"] = "FINDME",
                            ["directory_path"] = ws.Path
                        });
                    TestAssert.Equal(0, hitResult.ExitCode, "hit exit");
                    TestAssert.Contains(hitResult.Text, "FINDME", "hit text");
                    TestAssert.True(!hitResult.Text.Contains("node_modules"), "excludes node_modules");

                    ToolInvokeResult missResult = ToolClient.InvokeProduct(Exe, "grep_search",
                        new JObject
                        {
                            ["pattern"] = "NO_SUCH_TOKEN_XYZ",
                            ["directory_path"] = ws.Path
                        });
                    TestAssert.Equal(0, missResult.ExitCode, "miss exit");
                    TestAssert.Contains(missResult.Text, "No matches found", "miss message");

                    ToolInvokeResult caseResult = ToolClient.InvokeProduct(Exe, "grep_search",
                        new JObject
                        {
                            ["pattern"] = "findme",
                            ["directory_path"] = ws.Path,
                            ["case_insensitive"] = "true"
                        });
                    TestAssert.Equal(0, caseResult.ExitCode, "case exit");
                    TestAssert.Contains(caseResult.Text, "FINDME", "case text");

                    ToolInvokeResult globResult = ToolClient.InvokeProduct(Exe, "grep_search",
                        new JObject
                        {
                            ["pattern"] = "FINDME",
                            ["directory_path"] = ws.Path,
                            ["file_pattern"] = "*.cs"
                        });
                    TestAssert.Equal(0, globResult.ExitCode, "glob exit");
                    TestAssert.Contains(globResult.Text, "code.cs", "glob cs");
                    TestAssert.True(!globResult.Text.Contains("hit.txt"), "glob skips txt");
                });
            }
        }
    }
}
