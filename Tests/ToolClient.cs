using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

public sealed class ToolInvokeResult
{
    public int ExitCode;
    public string Stdout;
    public string Stderr;
    public string Text;
    public string ImageBase64;
    public string ImageMime;
    public long ElapsedMs;
    public bool TimedOut;
}

public static class ToolClient
{
    private const int PipeDrainTimeoutMs = 500;
    private const int PipeCloseJoinTimeoutMs = 100;

    public static string ProductExe(string fileName)
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
    }

    public static bool ProductExists(string fileName)
    {
        return File.Exists(ProductExe(fileName));
    }

    public static ToolInvokeResult Invoke(
        string exePath,
        string toolName,
        JObject arguments,
        JObject config = null,
        int timeoutMs = 0)
    {
        if (!File.Exists(exePath))
            throw new TestFailureException("Product EXE not found: " + exePath);

        JObject root = new JObject();
        root["config"] = config ?? new JObject();
        root["arguments"] = arguments ?? new JObject();
        string stdin = root.ToString(Newtonsoft.Json.Formatting.None);

        TestLog.Detail("Invoke: " + exePath + " " + toolName);
        TestLog.Detail("stdin: " + stdin);

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = toolName ?? "",
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? ".",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        ToolInvokeResult result = new ToolInvokeResult();
        Stopwatch sw = Stopwatch.StartNew();

        using (Process process = Process.Start(psi))
        {
            string stdout = "";
            string stderr = "";
            Thread outThread = new Thread(() =>
            {
                try { stdout = process.StandardOutput.ReadToEnd(); }
                catch { }
            });
            Thread errThread = new Thread(() =>
            {
                try { stderr = process.StandardError.ReadToEnd(); }
                catch { }
            });
            outThread.IsBackground = true;
            errThread.IsBackground = true;
            outThread.Start();
            errThread.Start();

            process.StandardInput.Write(stdin);
            process.StandardInput.Close();

            bool exited;
            if (timeoutMs > 0)
                exited = process.WaitForExit(timeoutMs);
            else
            {
                process.WaitForExit();
                exited = true;
            }

            if (!exited)
            {
                result.TimedOut = true;
                try { process.Kill(); } catch { }
                try { process.WaitForExit(2000); } catch { }
            }

            if (!outThread.Join(PipeDrainTimeoutMs))
            {
                try { process.StandardOutput.Close(); } catch { }
            }
            if (!errThread.Join(PipeDrainTimeoutMs))
            {
                try { process.StandardError.Close(); } catch { }
            }
            outThread.Join(PipeCloseJoinTimeoutMs);
            errThread.Join(PipeCloseJoinTimeoutMs);
            sw.Stop();

            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.Stdout = stdout ?? "";
            result.Stderr = stderr ?? "";
            result.ExitCode = exited && !result.TimedOut ? process.ExitCode : -1;

            ParseStdoutJson(result);
        }

        TestRunner.AddProcessMs(result.ElapsedMs);
        TestLog.Detail("exit=" + result.ExitCode + " elapsed_ms=" + result.ElapsedMs +
            (result.TimedOut ? " TIMED_OUT" : ""));
        TestLog.Detail("stdout: " + TestLog.FormatStdoutForLog(result.Stdout));
        if (!string.IsNullOrEmpty(result.Stderr))
            TestLog.Detail("stderr: " + result.Stderr);

        return result;
    }

    public static ToolInvokeResult InvokeProduct(
        string exeFileName,
        string toolName,
        JObject arguments,
        JObject config = null,
        int timeoutMs = 0)
    {
        return Invoke(ProductExe(exeFileName), toolName, arguments, config, timeoutMs);
    }

    public static void AssertUnknownTool(string exeFileName)
    {
        ToolInvokeResult r = InvokeProduct(exeFileName, "not_a_real_tool", new JObject());
        AssertToolError(r, "unknown tool");
        TestAssert.Equal(1, r.ExitCode, "unknown tool exit code");
    }

    public static void AssertToolError(ToolInvokeResult r, string contains)
    {
        TestAssert.True(r.ExitCode != 0, "non-zero exit");
        string text = r.Text ?? r.Stdout ?? "";
        TestAssert.True(
            text.StartsWith("error:", StringComparison.OrdinalIgnoreCase),
            "error: prefix, got: " + text);
        if (!string.IsNullOrEmpty(contains))
            TestAssert.ContainsIgnoreCase(text, contains, "error text");
    }

    private static void ParseStdoutJson(ToolInvokeResult result)
    {
        result.Text = result.Stdout ?? "";
        string trimmed = (result.Stdout ?? "").Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return;
        try
        {
            JObject obj = JObject.Parse(trimmed);
            JToken textToken = obj["text"];
            if (textToken != null && textToken.Type != JTokenType.Null)
                result.Text = textToken.Type == JTokenType.String
                    ? (textToken.Value<string>() ?? "")
                    : textToken.ToString();
            else
                result.Text = "";

            JObject image = obj["image"] as JObject;
            if (image != null)
            {
                result.ImageBase64 = (string)image["data"];
                result.ImageMime = (string)image["mime"];
            }
        }
        catch
        {
            // keep raw stdout as Text
        }
    }
}
