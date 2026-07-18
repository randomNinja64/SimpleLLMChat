using System;
using System.Globalization;

/// <summary>
/// Named-pipe status channel: CLI hosts SimpleLLMChat.Status.{pid}; GUI connects by child PID.
/// Wire format lines (UTF-8, one per message):
///   STATUS tokens=1234
///   STATUS indexing=start total=42
///   STATUS indexing=progress current=7 total=42 file=notes.md
///   STATUS indexing=done files=5
///   STATUS indexing=cleared
///   STATUS indexing=error message=...
/// </summary>
public static partial class StatusPipe
{
    public const string PipeNamePrefix = "SimpleLLMChat.Status.";

    public static string GetPipeName(int processId)
    {
        return PipeNamePrefix + processId.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryStripPrefix(string line, string prefix, out string rest)
    {
        rest = null;
        if (string.IsNullOrEmpty(line))
            return false;

        line = line.Trim();
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        rest = line.Substring(prefix.Length).Trim();
        return true;
    }

    private static string SanitizeToken(string value)
    {
        if (value == null)
            return string.Empty;
        return value.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static int ParseIntArg(string args, string key)
    {
        string raw = ParseStringArg(args, key);
        int value;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return value;
        return 0;
    }

    private static string ParseStringArg(string args, string key)
    {
        if (string.IsNullOrEmpty(args))
            return string.Empty;

        string needle = key + "=";
        int start = args.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return string.Empty;

        start += needle.Length;

        // Error messages (and similar free-text) may contain spaces; take the rest of the line.
        if (string.Equals(key, "message", StringComparison.OrdinalIgnoreCase))
            return args.Substring(start).Trim();

        int end = start;
        while (end < args.Length && args[end] != ' ')
            end++;
        return args.Substring(start, end - start);
    }
}
