public static partial class ChatPipe
{
    public const string SessionPrefix = "STATUS session=";

    /// <summary>Sent after each auto-save, naming the file the session is saved to.</summary>
    public static string FormatSession(string path)
    {
        return SessionPrefix + EncodePipeArg(path);
    }

    public static bool TryParseSessionLine(string line, out string path)
    {
        path = null;
        string rest;
        if (!TryStripPrefix(line, SessionPrefix, out rest) || rest.Length == 0)
            return false;

        path = DecodePipeArg(rest);
        return true;
    }
}
