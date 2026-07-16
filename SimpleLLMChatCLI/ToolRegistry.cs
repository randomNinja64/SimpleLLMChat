using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SimpleLLMChatCLI
{
    public class ToolRegistry
    {
        public class ToolCall
        {
            public string Id;
            public string Name;
            public string Arguments;

            public ToolCall(string name, string arguments, string id = "")
            {
                Id = id;
                Name = name;
                Arguments = arguments;
            }

            public ToolCall()
            {
                Id = "";
                Name = "";
                Arguments = "";
            }
        }

        public struct ToolParameterInfo
        {
            public string Name;
            public string Type;
            public string Description;
            public bool Required;
        }

        public struct ToolDefinition
        {
            public string Name;
            public string Description;
            public List<ToolParameterInfo> Parameters;
            public string ExecutablePath;
        }

        public readonly Dictionary<string, ToolDefinition> Tools = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);

        // Context injectors declared by manifests via the "context_injector" field (executablePath -> commandName).
        private readonly Dictionary<string, string> contextInjectorsByExecutable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Default values for options declared in manifests, keyed by lowercase name
        private readonly Dictionary<string, string> optionDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Config values to pass to tool executables
        private readonly ConfigHandler config;

        public ToolRegistry(ConfigHandler config)
        {
            this.config = config;
        }

        /// <summary>
        /// Clears loaded tools, context injectors, and option defaults so they can be reloaded.
        /// </summary>
        public void Clear()
        {
            Tools.Clear();
            contextInjectorsByExecutable.Clear();
            optionDefaults.Clear();
        }

        /// <summary>
        /// Scans a directory for *.json tool manifests and loads all tool definitions.
        /// </summary>
        public void LoadToolsFromDirectory(string toolsDir)
        {
            foreach (string jsonFile in ManifestScanner.GetManifestFiles(toolsDir))
            {
                try
                {
                    LoadManifest(jsonFile);
                }
                catch
                {
                    // Skip malformed manifests
                }
            }
        }

        /// <summary>
        /// Replaces {config.key} placeholders in text with actual config values.
        /// Also handles {config.key*N} for simple multiplication.
        /// </summary>
        private string ReplacePlaceholders(string text)
        {
            if (string.IsNullOrEmpty(text) || config == null)
                return text;

            return Regex.Replace(text, @"\{config\.(\w+?)(?:\*(\d+))?\}", match =>
            {
                string key = match.Groups[1].Value;
                string value = config.GetConfigString(key);
                if (value == null)
                    return match.Value; // Leave unresolved placeholders as-is

                if (match.Groups[2].Success)
                {
                    int multiplier;
                    int intValue;
                    if (int.TryParse(match.Groups[2].Value, out multiplier) && int.TryParse(value, out intValue))
                        return (intValue * multiplier).ToString();
                }

                return value;
            });
        }

        private void LoadManifest(string jsonFilePath)
        {
            string json = File.ReadAllText(jsonFilePath, Encoding.UTF8);
            JObject manifest = JObject.Parse(json);

            string executable = (string)manifest["executable"] ?? "";
            string manifestDir = Path.GetDirectoryName(jsonFilePath);
            string executablePath = Path.Combine(manifestDir, executable);

            string contextInjectorCommand = (string)manifest["context_injector"];
            if (!string.IsNullOrEmpty(contextInjectorCommand))
                contextInjectorsByExecutable[executablePath] = contextInjectorCommand;

            JArray toolsArray = manifest["tools"] as JArray;
            if (toolsArray == null)
                return;

            // Load option defaults from manifest
            JArray optionsArray = manifest["options"] as JArray;
            if (optionsArray != null)
            {
                foreach (JObject optObj in optionsArray)
                {
                    string optName = (string)optObj["name"];
                    string optDefault = (string)optObj["default"];
                    if (!string.IsNullOrEmpty(optName) && optDefault != null)
                        optionDefaults[optName] = optDefault;
                }
            }

            foreach (JObject toolObj in toolsArray)
            {
                string name = (string)toolObj["name"];
                if (string.IsNullOrEmpty(name))
                    continue;

                ToolDefinition def = new ToolDefinition
                {
                    Name = name,
                    Description = ReplacePlaceholders((string)toolObj["description"] ?? ""),
                    ExecutablePath = executablePath,
                    Parameters = new List<ToolParameterInfo>()
                };

                JArray paramsArray = toolObj["parameters"] as JArray;
                if (paramsArray != null)
                {
                    foreach (JObject paramObj in paramsArray)
                    {
                        def.Parameters.Add(new ToolParameterInfo
                        {
                            Name = (string)paramObj["name"] ?? "",
                            Type = (string)paramObj["type"] ?? "string",
                            Description = ReplacePlaceholders((string)paramObj["description"] ?? ""),
                            Required = paramObj["required"]?.Value<bool>() ?? false
                        });
                    }
                }

                Tools[name] = def;
            }
        }

        /// <summary>
        /// Builds the OpenAI-compatible tools JSON array for the given enabled tool names.
        /// </summary>
        public JArray BuildToolsArray(List<string> enabledTools)
        {
            JArray toolsArray = new JArray();

            foreach (string toolName in enabledTools)
            {
                ToolDefinition def;
                if (!Tools.TryGetValue(toolName, out def))
                    continue;

                JObject props = new JObject();
                JArray required = new JArray();

                foreach (var param in def.Parameters)
                {
                    JObject propObj = new JObject
                    {
                        ["type"] = param.Type,
                        ["description"] = param.Description
                    };
                    props[param.Name] = propObj;

                    if (param.Required)
                        required.Add(param.Name);
                }

                JObject parameters = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = props,
                    ["required"] = required
                };

                JObject func = new JObject
                {
                    ["name"] = def.Name,
                    ["description"] = def.Description,
                    ["parameters"] = parameters
                };

                JObject tool = new JObject
                {
                    ["type"] = "function",
                    ["function"] = func
                };

                toolsArray.Add(tool);
            }

            return toolsArray;
        }

        /// <summary>
        /// Calls each package's context injector for packages that have at least
        /// one enabled tool, and returns all non-empty results for injection into the system prompt.
        /// </summary>
        public List<string> GetContextInjections(List<string> enabledTools)
        {
            var results = new List<string>();

            if (enabledTools == null || enabledTools.Count == 0 || contextInjectorsByExecutable.Count == 0)
                return results;

            // Determine which executables are active (have at least one enabled tool).
            var activeExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string toolName in enabledTools)
            {
                ToolDefinition def;
                if (Tools.TryGetValue(toolName, out def) && !string.IsNullOrEmpty(def.ExecutablePath))
                    activeExecutables.Add(def.ExecutablePath);
            }

            foreach (var kvp in contextInjectorsByExecutable)
            {
                if (!activeExecutables.Contains(kvp.Key))
                    continue;

                string output = InvokeContextProvider(kvp.Key, kvp.Value);
                if (!string.IsNullOrWhiteSpace(output))
                    results.Add(output.Trim());
            }

            return results;
        }

        private string InvokeContextProvider(string executablePath, string commandName)
        {
            try
            {
                JObject stdinPayload = new JObject();
                stdinPayload["arguments"] = new JObject();

                JObject configObj = new JObject();
                foreach (var kvp in optionDefaults)
                    configObj[kvp.Key] = kvp.Value;
                foreach (var kvp in config.GetAllValues())
                    configObj[kvp.Key] = kvp.Value;
                stdinPayload["config"] = configObj;

                string stdinData = stdinPayload.ToString(Newtonsoft.Json.Formatting.None);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = commandName,
                    WorkingDirectory = Path.GetDirectoryName(executablePath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process process = Process.Start(psi))
                {
                    process.StandardInput.Write(stdinData);
                    process.StandardInput.Close();

                    var stdoutTask = Task.Factory.StartNew(() => process.StandardOutput.ReadToEnd());
                    process.WaitForExit();
                    return stdoutTask.Result;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Executes a tool by spawning its executable with the tool name as argv[1]
        /// and passing argument JSON + config via stdin.
        /// </summary>
        public void ExecuteToolCall(string toolName, string arguments, out string toolContent, out int exitCode)
        {
            toolContent = "";
            exitCode = 0;

            ToolDefinition def;
            if (!Tools.TryGetValue(toolName, out def))
            {
                toolContent = FormatCommandResult(toolName, "error: unknown tool '" + toolName + "'.", 1);
                exitCode = 1;
                return;
            }

            try
            {
                // Build stdin payload: arguments + config
                JObject stdinPayload = new JObject();
                stdinPayload["arguments"] = string.IsNullOrWhiteSpace(arguments) ? new JObject() : JToken.Parse(arguments);

                // Pass all config values to the tool, with manifest defaults as fallback
                JObject configObj = new JObject();
                foreach (var kvp in optionDefaults)
                    configObj[kvp.Key] = kvp.Value;
                foreach (var kvp in config.GetAllValues())
                    configObj[kvp.Key] = kvp.Value;
                stdinPayload["config"] = configObj;

                string stdinData = stdinPayload.ToString(Newtonsoft.Json.Formatting.None);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = def.ExecutablePath,
                    Arguments = toolName,
                    WorkingDirectory = Path.GetDirectoryName(def.ExecutablePath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                // Determine per-tool timeout from config (key: tooltimeout.<toolname>)
                string timeoutVal = config.GetConfigString("tooltimeout." + toolName.ToLowerInvariant());
                int timeoutSecs = 0;
                int.TryParse(timeoutVal, out timeoutSecs);
                int timeoutMs = timeoutSecs > 0 ? timeoutSecs * 1000 : 0;

                using (Process process = Process.Start(psi))
                {
                    // Write arguments via stdin
                    process.StandardInput.Write(stdinData);
                    process.StandardInput.Close();

                    // Read stdout/stderr asynchronously to avoid deadlock
                    var stdoutTask = Task.Factory.StartNew(() => process.StandardOutput.ReadToEnd());
                    var stderrTask = Task.Factory.StartNew(() => process.StandardError.ReadToEnd());

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
                        try { process.Kill(); } catch { }
                        toolContent = FormatCommandResult(toolName, "error: tool call timed out after " + timeoutSecs + " second(s).", -1);
                        exitCode = -1;
                        return;
                    }

                    string stdout = stdoutTask.Result;
                    string stderr = stderrTask.Result;
                    exitCode = process.ExitCode;

                    string output = stdout;
                    if (!string.IsNullOrEmpty(stderr))
                        output += stderr;
                    toolContent = FormatCommandResult(toolName, output, exitCode);
                }
            }
            catch (Exception ex)
            {
                exitCode = -1;
                toolContent = FormatCommandResult(toolName, "error: " + ex.Message, exitCode);
            }
        }

        /// <summary>
        /// Formats tool output consistently.
        /// </summary>
        public static string FormatCommandResult(string command, string output, int exitCode)
        {
            return "Command: " + command + "\nExit Code: " + exitCode + "\nOutput:\n" + output;
        }
    }
}
