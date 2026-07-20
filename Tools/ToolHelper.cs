using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

public struct ToolResult
{
    public string Output;
    public int ExitCode;

    public ToolResult(string output, int exitCode)
    {
        Output = output;
        ExitCode = exitCode;
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
        string output = "";

        try
        {
            ToolResult result = dispatch(toolName, argumentsJson);
            output = result.Output;
            exitCode = result.ExitCode;
        }
        catch (Exception e)
        {
            output = "error: " + e.Message;
            exitCode = 1;
        }

        Console.Write(output);
        return exitCode;
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

    public static string GetConfigString(string key)
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

    public static string GetRequiredArg(string arguments, string argName)
    {
        string value = JsonExtractString(arguments, argName)?.Trim() ?? "";
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("missing '" + argName + "' argument.");
        return value;
    }

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
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;

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
}
