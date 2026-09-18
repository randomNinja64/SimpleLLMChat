using System;

public static partial class StatusPipe
{
    public const string ApprovalPrefix = "STATUS approval ";

    public static string FormatApproval(string toolName, string arguments)
    {
        return ApprovalPrefix
            + "name=" + SanitizeToken(toolName)
            + " args=" + EncodePipeArg(arguments);
    }

    public static bool TryParseApprovalLine(string line, out string toolName, out string arguments)
    {
        toolName = null;
        arguments = null;

        string rest;
        if (!TryStripPrefix(line, ApprovalPrefix, out rest))
            return false;

        toolName = ParseStringArg(rest, "name");
        if (string.IsNullOrEmpty(toolName))
            return false;

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
