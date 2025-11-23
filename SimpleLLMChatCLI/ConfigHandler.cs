using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class ConfigHandler
{
    private Dictionary<string, string> configMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public ConfigHandler(string filename)
    {
        LoadConfig(filename);
    }

    private void LoadConfig(string filename)
    {
        if (!File.Exists(filename))
        {
            Console.Error.WriteLine("Failed to open config file: " + filename);
            return;
        }

        using (var reader = new StreamReader(filename, Encoding.UTF8, true))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrEmpty(line) || line.TrimStart().StartsWith("#"))
                    continue;

                var pos = line.IndexOf('=');
                if (pos != -1)
                {
                    string key = line.Substring(0, pos).Trim();
                    string value = line.Substring(pos + 1).Trim();
                    configMap[key] = value;
                }
            }
        }
    }

    // Generic helper methods to reduce repetition
    private string GetConfigValue(string key, string defaultValue = "")
    {
        return configMap.ContainsKey(key) ? configMap[key] : defaultValue;
    }

    private int GetConfigInt(string key, int defaultValue)
    {
        if (configMap.ContainsKey(key) && int.TryParse(configMap[key], out int result))
            return result;
        return defaultValue;
    }

    private bool GetConfigBool(string key, bool defaultValue = false)
    {
        if (configMap.ContainsKey(key) && int.TryParse(configMap[key], out int result))
            return result == 1;
        return defaultValue;
    }

    // Public getter methods
    public string GetLLMEndpoint() => GetConfigValue("llmserver");
    public string GetApiKey() => GetConfigValue("apiKey");
    public string GetModel() => GetConfigValue("model");
    public string GetSysPrompt() => GetConfigValue("sysprompt");
    public string GetAssistantName() => GetConfigValue("assistantname");
    public string GetSearxNGInstance() => GetConfigValue("searxnginstance");
    public bool GetShowToolOutput() => GetConfigBool("showtooloutput", false);
    public int GetMaxContentLength() => GetConfigInt("maxcontentlength", 8000);

    // Helper method to parse comma-separated list from config
    private List<string> GetConfigList(string key)
    {
        var list = new List<string>();

        if (!configMap.ContainsKey(key))
            return list;

        string value = configMap[key];
        var tokens = value.Split(',');

        foreach (var token in tokens)
        {
            string trimmed = token.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                list.Add(trimmed);
        }

        return list;
    }

    public List<string> getEnabledTools()
    {
        return GetConfigList("tools");
    }

    public List<string> getToolsRequiringApproval()
    {
        return GetConfigList("toolsrequiringapproval");
    }
}
