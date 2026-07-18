using System.Globalization;

public static partial class StatusPipe
{
    public const string TokensPrefix = "STATUS tokens=";

    public static bool TryParseStatusLine(string line, out int tokens)
    {
        tokens = 0;
        string value;
        if (!TryStripPrefix(line, TokensPrefix, out value))
            return false;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out tokens);
    }
}
