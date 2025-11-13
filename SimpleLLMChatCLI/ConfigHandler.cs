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
    public string GetLLMEndpoint()
    {
        return configMap.ContainsKey("llmserver") ? configMap["llmserver"] : "http://127.0.0.1";
    }

    public string GetApiKey()
    {
        return configMap.ContainsKey("apiKey") ? configMap["apiKey"] : "";
    }

    public string GetModel()
    {
        return configMap.ContainsKey("model") ? configMap["model"] : "";
    }

    public string GetSysPrompt()
    {
        return configMap.ContainsKey("sysprompt") ? configMap["sysprompt"] : "";
    }

    public string GetAssistantName()
    {
        return configMap.ContainsKey("assistantname") ? configMap["assistantname"] : "";
    }

    public List<string> getEnabledTools()
    {
        var tools = new List<string>();

        if (!configMap.ContainsKey("tools"))
            return tools;

        string toolsLine = configMap["tools"];
        var tokens = toolsLine.Split(',');

        foreach (var token in tokens)
        {
            string trimmed = token.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                tools.Add(trimmed);
        }

        return tools;
    }

    public bool GetShowToolOutput()
    {
        if (configMap.ContainsKey("showtooloutput"))
        {
            string value = configMap["showtooloutput"];
            if (int.TryParse(value, out int result))
                return result == 1;
        }
        return false; // Default to false if not specified or invalid
    }
}
