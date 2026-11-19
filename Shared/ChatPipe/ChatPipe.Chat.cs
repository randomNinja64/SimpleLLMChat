using System;

/// <summary>
/// What a block of chat output is; the name is what STARTBLOCK carries.
/// How it is shown (tags, collapsible sections) is up to each front end.
/// </summary>
public enum ChatBlockKind
{
    Thinking,
    ToolCall,
    ToolOutput
}

public static partial class ChatPipe
{
    public const string TextPrefix = "TEXT ";
    public const string StartBlockPrefix = "STARTBLOCK ";
    public const string EndBlockLine = "ENDBLOCK";

    public static bool TryParseTextLine(string line, out string text)
    {
        text = null;
        // No trimming: leading and trailing whitespace is chat content.
        if (line == null || !line.StartsWith(TextPrefix, StringComparison.Ordinal))
            return false;

        text = DecodePipeArg(line.Substring(TextPrefix.Length));
        return true;
    }

    /// <summary>False for a kind this build doesn't know; its text then shows as plain text.</summary>
    public static bool TryParseBlockLine(string line, out ChatBlockKind kind, out string name)
    {
        kind = ChatBlockKind.Thinking;
        name = null;

        string rest;
        if (!TryStripPrefix(line, StartBlockPrefix, out rest))
            return false;

        int space = rest.IndexOf(' ');
        name = space < 0 ? string.Empty : rest.Substring(space + 1).Trim();
        // IsDefined: TryParse alone also accepts numbers.
        return Enum.TryParse(space < 0 ? rest : rest.Substring(0, space), out kind)
            && Enum.IsDefined(typeof(ChatBlockKind), kind);
    }

    public static bool TryParseEndBlockLine(string line)
    {
        return line != null && string.Equals(line.Trim(), EndBlockLine, StringComparison.Ordinal);
    }
}
