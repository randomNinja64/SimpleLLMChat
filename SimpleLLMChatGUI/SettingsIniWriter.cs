using System;
using System.Collections.Generic;
using System.IO;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// Shared LLMSettings.ini writer for Options (including first-run setup mode).
    /// </summary>
    public static class SettingsIniWriter
    {
        /// <summary>
        /// Escapes a system prompt for INI storage (backslash, newlines, tabs).
        /// </summary>
        public static string EscapePromptForStorage(string prompt)
        {
            if (string.IsNullOrEmpty(prompt))
                return string.Empty;

            return prompt.Replace("\\", "\\\\")
                        .Replace("\r\n", "\\n")
                        .Replace("\n", "\\n")
                        .Replace("\r", "\\r")
                        .Replace("\t", "\\t");
        }

        private static void AddIniSection(List<string> allLines, string name, List<string> lines)
        {
            lines.Sort(StringComparer.OrdinalIgnoreCase);
            if (allLines.Count > 0)
                allLines.Add(string.Empty);
            allLines.Add("[" + name + "]");
            allLines.AddRange(lines);
        }

        /// <summary>
        /// Writes an INI file from an ordered list of (section name, lines) pairs.
        /// This is the single place that touches disk for LLMSettings.ini.
        /// </summary>
        public static void WriteSections(string path, IEnumerable<KeyValuePair<string, List<string>>> sections)
        {
            var allLines = new List<string>();
            foreach (var section in sections)
                AddIniSection(allLines, section.Key, section.Value);
            File.WriteAllLines(path, allLines);
        }

        /// <summary>
        /// Writes a complete LLMSettings.ini with the given System values
        /// and defaults for Appearance, RAG, and Tools.
        /// </summary>
        public static void WriteInitialConfig(
            string path,
            string apiKey,
            string llmServer,
            string model,
            string sysPrompt,
            int contextWindowSize)
        {
            var sections = new List<KeyValuePair<string, List<string>>>
            {
                new KeyValuePair<string, List<string>>(AppSettings.SectionAppearance, AppSettings.DefaultLines(AppSettings.SectionAppearance)),
                new KeyValuePair<string, List<string>>(AppSettings.SectionRag, AppSettings.DefaultLines(AppSettings.SectionRag)),
                new KeyValuePair<string, List<string>>(AppSettings.SectionSystem, GetSystemSettings(apiKey, llmServer, model, sysPrompt, contextWindowSize)),
                new KeyValuePair<string, List<string>>(AppSettings.SectionTools, AppSettings.DefaultLines(AppSettings.SectionTools)),
            };
            WriteSections(path, sections);
        }

        public static List<string> GetSystemSettings(
            string apiKey,
            string llmServer,
            string model,
            string sysPrompt,
            int contextWindowSize)
        {
            return new List<string>
            {
                AppSettings.ApiKey.Line(apiKey ?? string.Empty),
                AppSettings.ContextWindowSize.Line(contextWindowSize),
                AppSettings.LlmServer.Line(llmServer ?? string.Empty),
                AppSettings.Model.Line(model ?? string.Empty),
                AppSettings.SysPrompt.Line("\"" + EscapePromptForStorage(sysPrompt) + "\""),
            };
        }

    }
}
