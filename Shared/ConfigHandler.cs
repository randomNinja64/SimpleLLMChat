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

    public string GetConfigValue(string key, string defaultValue = "")
    {
        return configMap.ContainsKey(key) ? configMap[key] : defaultValue;
    }

    public int GetConfigInt(string key, int defaultValue)
    {
        if (configMap.ContainsKey(key) && int.TryParse(configMap[key], out int result))
            return result;
        return defaultValue;
    }

    public bool GetConfigBool(string key, bool defaultValue = false)
    {
        if (configMap.ContainsKey(key) && int.TryParse(configMap[key], out int result))
            return result == 1;
        return defaultValue;
    }

    public List<string> GetConfigList(string key)
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
    /// Returns the string value for any config key.
    /// Returns null if the key has no value. Used for placeholder replacement in tool manifests.
    /// </summary>
    public string GetConfigString(string key)
    {
        string val = GetConfigValue(key);
        return string.IsNullOrEmpty(val) ? null : val;
    }

    /// <summary>
    /// Returns all config key-value pairs. Used by ToolRegistry to forward config to tool processes.
    /// </summary>
    public Dictionary<string, string> GetAllValues()
    {
        return new Dictionary<string, string>(configMap, StringComparer.OrdinalIgnoreCase);
    }
}
