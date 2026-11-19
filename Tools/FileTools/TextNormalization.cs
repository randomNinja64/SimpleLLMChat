using System.IO;
using System.Text;

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

        /// <summary>
        /// Reads a text file and reports the encoding to write it back with: a BOM decides
        /// when present; otherwise valid UTF-8 stays BOM-less UTF-8 and anything else is
        /// treated as the system ANSI code page.
        /// </summary>
        internal static string ReadDetectingEncoding(string path, out Encoding encoding)
        {
            try
            {
                // Strict UTF-8, so a file that is not UTF-8 throws instead of decoding to U+FFFD.
                using (StreamReader reader = new StreamReader(path, new UTF8Encoding(false, true), true))
                {
                    string text = reader.ReadToEnd();
                    encoding = reader.CurrentEncoding;
                    return text;
                }
            }
            catch (DecoderFallbackException)
            {
                encoding = Encoding.Default;
                return File.ReadAllText(path, encoding);
            }
        }

        /// <summary>
        /// Encoding for writing a whole file: the one its BOM names when the file already
        /// exists with a BOM, otherwise UTF-8 without a BOM.
        /// </summary>
        internal static Encoding EncodingForOverwrite(string path)
        {
            Encoding utf8NoBom = new UTF8Encoding(false);
            if (!File.Exists(path))
                return utf8NoBom;

            using (StreamReader reader = new StreamReader(path, utf8NoBom, true))
            {
                // A BOM is detected on the first read; without one the encoding stays as given.
                reader.Peek();
                return reader.CurrentEncoding;
            }
        }
    }
}
