using System;
using System.Globalization;

public static partial class StatusPipe
{
    public const string ApprovalPrefix = "STATUS approval ";

    public static string FormatApproval(string toolName, string arguments, double? confidence = null)
    {
        string line = ApprovalPrefix + "name=" + SanitizeToken(toolName);
        if (confidence.HasValue)
            line += " confidence=" + confidence.Value.ToString("0.00", CultureInfo.InvariantCulture);
        line += " args=" + EncodePipeArg(arguments);
        return line;
    }

    public static bool TryParseApprovalLine(string line, out string toolName, out string arguments, out string confidence)
    {
        toolName = null;
        arguments = null;
        confidence = null;

        string rest;
        if (!TryStripPrefix(line, ApprovalPrefix, out rest))
            return false;

        toolName = ParseStringArg(rest, "name");
        if (string.IsNullOrEmpty(toolName))
            return false;

        confidence = ParseStringArg(rest, "confidence");
        arguments = DecodePipeArg(ParseStringArg(rest, "args"));
        return true;
    }

    private static string EncodePipeArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static string DecodePipeArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                char next = value[i + 1];
                if (next == 'n') { sb.Append('\n'); i++; continue; }
                if (next == 'r') { sb.Append('\r'); i++; continue; }
                if (next == '\\') { sb.Append('\\'); i++; continue; }
            }
            sb.Append(value[i]);
        }
        return sb.ToString();
    }
}
