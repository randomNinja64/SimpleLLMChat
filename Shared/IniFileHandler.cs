using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Unified INI file parser for both CLI and GUI projects.
/// Handles loading and parsing of INI configuration files with support for:
/// - Case-insensitive keys
/// - Comments (lines starting with #)
/// - Empty lines
/// - UTF-8 encoding with BOM detection
/// </summary>
public static class IniFileHandler
{
    /// <summary>
    /// Loads an INI file and returns a dictionary of key-value pairs.
    /// Ignores empty lines, comments (lines starting with #), and invalid lines.
    /// </summary>
    /// <param name="path">Path to the INI file</param>
    /// <returns>Dictionary with case-insensitive keys. Returns empty dictionary if file doesn't exist.</returns>
    public static Dictionary<string, string> LoadIni(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(path))
        {
            return dict;
        }

        // Use StreamReader for memory efficiency and explicit UTF-8 encoding
        using (var reader = new StreamReader(path, Encoding.UTF8, true))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                
                // Skip empty lines and comments
                if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                    continue;

                // Skip lines without equals sign
                if (!trimmed.Contains("="))
                    continue;

                // Split on first equals sign only (value may contain =)
                string[] parts = trimmed.Split(new char[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim();
                    string val = parts[1].Trim();
                    
                    // Only add non-empty keys
                    if (!string.IsNullOrEmpty(key))
                    {
                        dict[key] = val;
                    }
                }
            }
        }

        return dict;
    }
}

