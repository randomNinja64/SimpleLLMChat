using System;

/// <summary>
/// Shared tool-approval prompt text for the terminal prompt and the GUI message box.
/// </summary>
public static class ToolApproval
{
    public const string RunToolPrefix = "Run tool: ";
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
}
