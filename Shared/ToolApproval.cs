using System;
using System.Text;

/// <summary>
/// Shared tool-approval prompt text used by CLI output and GUI parsing.
/// </summary>
public static class ToolApproval
{
    public const string RunToolPrefix = "Run tool: ";
    public const string ArgumentsPrefix = "With arguments:";
    public const string ApprovalPrompt = "Approve? (Y/N): ";

    public static string UnescapeArguments(string arguments)
    {
        if (string.IsNullOrEmpty(arguments))
            return string.Empty;

        return arguments
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t")
            .Replace("\\\"", "\"")
            .Replace("\\'", "'")
            .Replace("\\\\", "\\");
    }

    public static string FormatApprovalMessage(string toolName, string arguments)
    {
        string formattedArguments = UnescapeArguments(arguments);
        return RunToolPrefix + toolName + "\n\n" + ArgumentsPrefix + "\n" + formattedArguments + "?";
    }

    public static bool RequestApproval(string toolName, string arguments)
    {
        Console.WriteLine();
        Console.WriteLine(FormatApprovalMessage(toolName, arguments));
        Console.Out.Flush();

        while (true)
        {
            Console.Write(ApprovalPrompt);
            Console.Out.Flush();

            string input = Console.ReadLine();
            if (input == null)
                continue;

            input = input.Trim();
            if (input.Length == 0)
                continue;

            if (string.Equals(input, "Y", StringComparison.OrdinalIgnoreCase)
                || string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(input, "N", StringComparison.OrdinalIgnoreCase)
                || string.Equals(input, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Console.WriteLine("Please enter Y or N.");
        }
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

        int argsLabel = block.IndexOf(ArgumentsPrefix, nameEnd, StringComparison.Ordinal);
        if (argsLabel < 0)
            return false;

        int argsStart = block.IndexOf('\n', argsLabel);
        if (argsStart < 0)
            return false;
        argsStart++;

        string argsPart = block.Substring(argsStart).TrimEnd('\r', '\n', ' ');
        if (!argsPart.EndsWith("?", StringComparison.Ordinal))
            return false;

        arguments = argsPart.Substring(0, argsPart.Length - 1);
        return true;
    }
}
