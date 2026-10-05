using System;

/// <summary>
/// Shared tool-approval prompt text for TTY stdout and GUI MessageBox formatting.
/// GUI turn/approval control uses StatusPipe (STATUS approval), not stdout scraping.
/// </summary>
public static class ToolApproval
{
    public const string RunToolPrefix = "Run tool: ";
    public const string ArgumentsPrefix = "With arguments:";
    public const string ApprovalPrompt = "Approve? (Y/N): ";
    public const string ConfidencePrefix = "Jev confidence: ";

    public static string UnescapeArguments(string arguments)
    {
        if (string.IsNullOrEmpty(arguments))
            return string.Empty;

        // Collapse literal backslashes first so path fragments like \test
        // are not mistaken for \t / \n / \r escapes.
        return arguments
            .Replace("\\\\", "\u0000")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t")
            .Replace("\\\"", "\"")
            .Replace("\\'", "'")
            .Replace("\u0000", "\\");
    }

    public static string FormatApprovalMessage(string toolName, string arguments, string confidenceText = null)
    {
        string formattedArguments = UnescapeArguments(arguments);
        string message = RunToolPrefix + toolName + "\n";
        if (!string.IsNullOrEmpty(confidenceText))
            message += ConfidencePrefix + confidenceText + "\n";
        message += formattedArguments;
        return message;
    }

    public static bool TryParseApprovalPrompt(string text, out string toolName, out string arguments)
    {
        toolName = null;
        arguments = null;

        if (string.IsNullOrEmpty(text))
            return false;

        int promptIndex = text.LastIndexOf(ApprovalPrompt, StringComparison.Ordinal);
        if (promptIndex < 0)
            return false;

        string block = text.Substring(0, promptIndex);
        int runIndex = block.LastIndexOf(RunToolPrefix, StringComparison.Ordinal);
        if (runIndex < 0)
            return false;

        int nameStart = runIndex + RunToolPrefix.Length;
        int nameEnd = block.IndexOf('\n', nameStart);
        if (nameEnd < 0)
            return false;

        toolName = block.Substring(nameStart, nameEnd - nameStart).Trim();

        string rest = block.Substring(nameEnd + 1);

        if (rest.StartsWith(ConfidencePrefix, StringComparison.Ordinal))
        {
            int confidenceLineEnd = rest.IndexOf('\n');
            rest = confidenceLineEnd >= 0 ? rest.Substring(confidenceLineEnd + 1) : string.Empty;
        }

        // Older format inserted a "With arguments:" label before the payload.
        if (rest.StartsWith(ArgumentsPrefix, StringComparison.Ordinal))
        {
            int argsLineEnd = rest.IndexOf('\n');
            if (argsLineEnd < 0)
                return false;
            rest = rest.Substring(argsLineEnd + 1);
        }

        arguments = rest.TrimEnd('\r', '\n', ' ');
        return true;
    }
}
