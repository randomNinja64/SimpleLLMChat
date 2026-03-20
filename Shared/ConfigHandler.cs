using System;
using System.Collections.Generic;

/// <summary>
/// Shared configuration handler for both CLI and GUI projects.
/// Wraps IniFileHandler with typed getters for common settings.
/// </summary>
public class ConfigHandler
{
    private Dictionary<string, string> configMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public ConfigHandler(string filename)
    {
        LoadConfig(filename);
    }

    private void LoadConfig(string filename)
    {
        configMap = IniFileHandler.LoadIni(filename);
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

    /// <summary>
    /// Returns the string value for any config key, with proper defaults for int configs.
    /// Returns null if the key has no value. Used for placeholder replacement in tool manifests.
    /// </summary>
    public string GetConfigString(string key)
    {
        switch (key.ToLowerInvariant())
        {
            case "maxcontentlength": return GetMaxContentLength().ToString();
            case "maxsearchresults": return GetMaxSearchResults().ToString();
            case "fontsize": return GetFontSize().ToString();
            default:
                string val = GetConfigValue(key);
                return string.IsNullOrEmpty(val) ? null : val;
        }
    }

    // Public getter methods
    public string GetLLMEndpoint() => GetConfigValue("llmserver");
    public string GetApiKey() => GetConfigValue("apiKey");
    public string GetModel() => GetConfigValue("model");
    public string GetSysPrompt() => GetConfigValue("sysprompt");
    public string GetAssistantName() => GetConfigValue("assistantname");
    public string GetSearxNGInstance() => GetConfigValue("searxnginstance");
    public bool GetShowToolOutput() => GetConfigBool("showtooloutput", false);
    public bool GetShowReasoningOutput() => GetConfigBool("showreasoningoutput", false);
    public int GetMaxContentLength() => GetConfigInt("maxcontentlength", AppConstants.DefaultMaxContentLength);
    public int GetMaxSearchResults() => GetConfigInt("maxsearchresults", AppConstants.DefaultMaxSearchResults);
    public bool GetMarkdownParsing() => GetConfigBool("markdownparsing", true);
    public string GetCustomFontFamily() => GetConfigValue("customfontfamily");
    public int GetFontSize() => GetConfigInt("fontsize", AppConstants.DefaultChatFontSize);
    public List<string> GetEnabledTools() => GetConfigList("tools");
    public List<string> GetToolsRequiringApproval() => GetConfigList("toolsrequiringapproval");
}
