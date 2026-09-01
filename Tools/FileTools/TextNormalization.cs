namespace FileTools
{
    internal static class TextNormalization
    {
        /// <summary>
        /// Normalizes CRLF and lone CR to LF for consistent text comparison and patching.
        /// </summary>
        internal static string NormalizeLineEndings(string text)
        {
            if (text == null) return null;
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
