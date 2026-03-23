using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// Scans tool manifest files for option definitions that external tools
    /// can register to appear in the Options menu.
    /// </summary>
    public class ToolOptionDefinition
    {
        public string Name;         // INI config key (e.g., "maxFileContentLength")
        public string Label;        // Display label for the UI
        public string Type;         // "string", "int", or "bool"
        public string Default;      // Default value as a string
        public string Source;       // Manifest/category name (e.g., "BuiltInTools")
    }

    public static class ToolOptionsRegistry
    {
        /// <summary>
        /// Scans the tools directory for *.json manifests and extracts
        /// all option definitions from their "options" arrays.
        /// </summary>
        public static List<ToolOptionDefinition> LoadOptionsFromDirectory(string toolsDir)
        {
            var options = new List<ToolOptionDefinition>();

            foreach (string jsonFile in ManifestScanner.GetManifestFiles(toolsDir))
            {
                try
                {
                    LoadManifestOptions(jsonFile, options);
                }
                catch
                {
                    // Skip malformed manifests
                }
            }

            return options;
        }

        private static void LoadManifestOptions(string jsonFilePath, List<ToolOptionDefinition> options)
        {
            string json = File.ReadAllText(jsonFilePath, Encoding.UTF8);
            JObject manifest = JObject.Parse(json);
            string source = (string)manifest["name"];
            if (string.IsNullOrWhiteSpace(source))
                source = Path.GetFileNameWithoutExtension(jsonFilePath) ?? "Tools";

            JArray optionsArray = manifest["options"] as JArray;
            if (optionsArray == null)
                return;

            foreach (JObject optObj in optionsArray)
            {
                string name = (string)optObj["name"];
                if (string.IsNullOrEmpty(name))
                    continue;

                options.Add(new ToolOptionDefinition
                {
                    Name = name,
                    Label = (string)optObj["label"] ?? name,
                    Type = ((string)optObj["type"] ?? "string").ToLowerInvariant(),
                    Default = (string)optObj["default"] ?? "",
                    Source = source
                });
            }
        }
    }
}
