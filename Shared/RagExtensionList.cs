using System;
using System.Collections.Generic;

/// <summary>Helpers for the ragAllowedExtensions whitelist.</summary>
public static class RagExtensionList
{
    public static string NormalizeExtension(string token)
    {
        if (string.IsNullOrEmpty(token))
            return null;
        string t = token.Trim();
        if (t.Length == 0)
            return null;
        if (t[0] != '.')
            t = "." + t;
        return t.ToLowerInvariant();
    }

    /// <summary>
    /// Parses a free-form extensions list (commas, semicolons, whitespace) into a
    /// normalized comma-separated INI value. Blank input uses the built-in default.
    /// </summary>
    public static string FormatForStorage(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return AppConstants.DefaultRagAllowedExtensions;

        string[] tokens = raw.Split(new[] { ',', ';', '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>();
        foreach (string token in tokens)
        {
            string ext = NormalizeExtension(token);
            if (!string.IsNullOrEmpty(ext))
                parts.Add(ext);
        }
        if (parts.Count == 0)
            return AppConstants.DefaultRagAllowedExtensions;
        return string.Join(",", parts.ToArray());
    }
}
