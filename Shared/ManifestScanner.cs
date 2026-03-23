using System.Collections.Generic;
using System.IO;

/// <summary>
/// Shared utility for discovering tool manifest files from a tools directory.
/// Used by both CLI (ToolRegistry) and GUI (Options, ToolOptionsRegistry) projects.
/// </summary>
public static class ManifestScanner
{
    /// <summary>
    /// Returns all *.json manifest file paths from a tools directory and one level of subdirectories.
    /// </summary>
    public static List<string> GetManifestFiles(string toolsDir)
    {
        var files = new List<string>();

        if (!Directory.Exists(toolsDir))
            return files;

        files.AddRange(Directory.GetFiles(toolsDir, "*.json"));

        foreach (string subDir in Directory.GetDirectories(toolsDir))
            files.AddRange(Directory.GetFiles(subDir, "*.json"));

        return files;
    }
}
