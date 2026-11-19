using System;

public static partial class ChatPipe
{
    public const string ReadyLine = "STATUS ready";

    public static bool TryParseReadyLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return false;
        return string.Equals(line.Trim(), ReadyLine, StringComparison.Ordinal);
    }
}
