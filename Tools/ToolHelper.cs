using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

public struct ToolResult
{
    public string Output;
    public int ExitCode;
    public string ImageBase64;
    public string ImageMime;

    public ToolResult(string output, int exitCode)
    {
        Output = output;
        ExitCode = exitCode;
        ImageBase64 = null;
        ImageMime = null;
    }

    public ToolResult(string output, int exitCode, string imageBase64, string imageMime)
    {
        Output = output;
        ExitCode = exitCode;
        ImageBase64 = imageBase64;
        ImageMime = imageMime;
    }
}

/// <summary>
/// Shared helper utilities for tool executable projects (FileTools, WebTools, etc.).
/// </summary>
public static class ToolHelper
{
    /// <summary>
    /// Common entry point for all tool executables. Handles encoding setup,
    /// manifest defaults, stdin JSON parsing, error handling, and output.
    /// </summary>
    public static int RunToolMain(string[] args, Func<string, string, ToolResult> dispatch)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        LoadManifestDefaults();

        if (args.Length < 1)
        {
            string exeName = Path.GetFileName(Assembly.GetEntryAssembly().Location);
            Console.Write("Usage: " + exeName + " <tool_name>\nArguments JSON is read from stdin.");
            return 1;
        }

        string toolName = args[0];
        string stdinData = Console.In.ReadToEnd();
        string argumentsJson = "";

        if (!string.IsNullOrWhiteSpace(stdinData))
        {
            try
            {
                JObject root = JObject.Parse(stdinData);

                JObject configObj = root["config"] as JObject;
                if (configObj != null)
                {
                    foreach (JProperty prop in configObj.Properties())
                        Config[prop.Name] = prop.Value.ToString();
                }

                JToken argsToken = root["arguments"];
                if (argsToken != null)
                    argumentsJson = argsToken.ToString();
            }
            catch
            {
                argumentsJson = stdinData;
            }
        }

        int exitCode = 0;
        ToolResult result = new ToolResult("", 0);

        try
        {
            result = dispatch(toolName, argumentsJson);
            exitCode = result.ExitCode;
        }
        catch (Exception e)
        {
            result = Fail(e.Message);
            exitCode = 1;
        }

        Console.Write(FormatResultJson(result));
        return exitCode;
    }

    public static ToolResult Fail(string message)
    {
        return new ToolResult("error: " + message, 1);
    }

    /// <summary>Serialize a tool result as the JSON-first stdout protocol.</summary>
    public static string FormatResultJson(ToolResult result)
    {
        var obj = new JObject();
        obj["text"] = result.Output ?? "";

        if (!string.IsNullOrEmpty(result.ImageBase64))
        {
            string mime = string.IsNullOrEmpty(result.ImageMime) ? "image/png" : result.ImageMime;
            obj["image"] = new JObject
            {
                ["mime"] = mime,
                ["data"] = result.ImageBase64
            };
        }

        return obj.ToString(Newtonsoft.Json.Formatting.None);
    }

    // Loads option defaults from the manifest file sitting next to the executable.
    // Must be called before stdin config is parsed so that stdin values take precedence.
    private static void LoadManifestDefaults()
    {
        try
        {
            string manifestPath = Path.ChangeExtension(
                Assembly.GetEntryAssembly().Location, ".json");
            if (!File.Exists(manifestPath)) return;
            JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
            foreach (JToken opt in manifest["options"] ?? new JArray())
            {
                string name = opt["name"]?.ToString();
                string def  = opt["default"]?.ToString();
                if (!string.IsNullOrEmpty(name) && def != null)
                    Config[name] = def;
            }
        }
        catch { }
    }

    // Config values passed via the "config" key in stdin JSON
    public static Dictionary<string, string> Config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static string GetConfigValue(string key)
    {
        string value;
        if (Config.TryGetValue(key, out value))
            return value;
        return "";
    }

    public static int GetConfigInt(string key, int defaultValue)
    {
        string value;
        if (Config.TryGetValue(key, out value))
        {
            int result;
            if (int.TryParse(value, out result))
                return result;
        }
        return defaultValue;
    }

    /// <summary>
    /// Resolves a data directory from config, or <paramref name="defaultFolderName"/>
    /// next to the tool EXE when the key is empty.
    /// </summary>
    public static string GetConfiguredDirectory(string configKey, string defaultFolderName)
    {
        string configured = GetConfigValue(configKey).Trim();
        if (!string.IsNullOrEmpty(configured))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, defaultFolderName));
    }

    public static string GetRequiredArg(string arguments, string argName)
    {
        string value = JsonExtractString(arguments, argName)?.Trim() ?? "";
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("missing '" + argName + "' argument.");
        return value;
    }

    // After the direct child exits, wait this long for stdout/stderr EOF.
    // GUI apps launched via `start` inherit redirected pipe handles and would
    // otherwise keep ReadToEnd blocked until those apps close.
    private const int PipeDrainTimeoutMs = 500;
    private const int PipeCloseJoinTimeoutMs = 100;

    public static string ExecuteProcess(string fileName, string arguments, out int exitCode, bool combineErrorOutput = true)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (Process process = Process.Start(psi))
            {
                string output = "";
                string error = "";
                Thread outThread = new Thread(() =>
                {
                    try { output = process.StandardOutput.ReadToEnd(); }
                    catch { }
                });
                Thread errThread = new Thread(() =>
                {
                    try { error = process.StandardError.ReadToEnd(); }
                    catch { }
                });
                outThread.IsBackground = true;
                errThread.IsBackground = true;
                outThread.Start();
                errThread.Start();

                process.WaitForExit();
                exitCode = process.ExitCode;

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

                if (combineErrorOutput && !string.IsNullOrEmpty(error))
                {
                    return output + error;
                }
                return output;
            }
        }
        catch (Exception ex)
        {
            exitCode = -1;
            throw new InvalidOperationException("Failed to execute " + fileName + ": " + ex.Message, ex);
        }
    }

    public static string JsonExtractString(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
        {
            return "";
        }

        try
        {
            string trimmedJson = json.Trim();
            if (trimmedJson.Length == 0)
            {
                return "";
            }

            JToken root = JToken.Parse(trimmedJson);
            if (root.Type == JTokenType.Object)
            {
                JObject obj = (JObject)root;
                JToken token;
                if (!obj.TryGetValue(key, out token))
                {
                    foreach (JProperty property in obj.Properties())
                    {
                        if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                        {
                            token = property.Value;
                            break;
                        }
                    }
                }

                if (token != null && token.Type != JTokenType.Null)
                {
                    return token.Type == JTokenType.String ? token.Value<string>() ?? "" : token.ToString();
                }
            }
        }
        catch
        {
        }

        return "";
    }

    public static JArray JsonExtractArray(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            return null;

        try
        {
            JToken root = JToken.Parse(json.Trim());
            if (root.Type != JTokenType.Object)
                return null;

            JObject obj = (JObject)root;
            JToken token;
            if (!obj.TryGetValue(key, out token))
            {
                foreach (JProperty property in obj.Properties())
                {
                    if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        token = property.Value;
                        break;
                    }
                }
            }

            return token as JArray;
        }
        catch
        {
            return null;
        }
    }
}
