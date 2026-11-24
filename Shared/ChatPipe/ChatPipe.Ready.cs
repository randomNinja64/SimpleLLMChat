using System;

public static partial class ChatPipe
{
    public const string ReadyLine = "STATUS ready";
    private const string WorkspaceArg = " workspace=";

    /// <summary>
    /// The ready line, naming the workspace tools run in. It carries the workspace
    /// because the ready line is what the pipe replays when the GUI connects late.
    /// </summary>
    public static string FormatReady(string workspace)
    {
        return string.IsNullOrEmpty(workspace) ? ReadyLine : ReadyLine + WorkspaceArg + EncodePipeArg(workspace);
    }

    public static bool TryParseReadyLine(string line)
    {
        string workspace;
        return TryParseReadyLine(line, out workspace);
    }

    /// <summary>Workspace is null when the line doesn't name one.</summary>
    public static bool TryParseReadyLine(string line, out string workspace)
    {
        workspace = null;
        if (string.IsNullOrEmpty(line))
            return false;

        line = line.Trim();
        if (string.Equals(line, ReadyLine, StringComparison.Ordinal))
            return true;
        if (!line.StartsWith(ReadyLine + WorkspaceArg, StringComparison.Ordinal))
            return false;

        workspace = DecodePipeArg(line.Substring(ReadyLine.Length + WorkspaceArg.Length));
        return true;
    }
}
