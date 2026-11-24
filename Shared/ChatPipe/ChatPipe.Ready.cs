using System;

public static partial class ChatPipe
{
    public const string ReadyPrefix = "STATUS ready workspace=";

    /// <summary>
    /// The ready line, naming the workspace tools run in. It carries the workspace
    /// because the ready line is what the pipe replays when the GUI connects late.
    /// </summary>
    public static string FormatReady(string workspace)
    {
        return ReadyPrefix + EncodePipeArg(workspace);
    }

    public static bool TryParseReadyLine(string line)
    {
        string workspace;
        return TryParseReadyLine(line, out workspace);
    }

    public static bool TryParseReadyLine(string line, out string workspace)
    {
        workspace = null;
        if (line == null || !line.StartsWith(ReadyPrefix, StringComparison.Ordinal))
            return false;

        workspace = DecodePipeArg(line.Substring(ReadyPrefix.Length).TrimEnd());
        return true;
    }
}
