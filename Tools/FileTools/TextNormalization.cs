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
            byte[] bytes = File.ReadAllBytes(path);

            // UTF-32 LE comes before UTF-16 LE, whose BOM is its first two bytes.
            foreach (Encoding withBom in BomEncodings)
            {
                byte[] bom = withBom.GetPreamble();
                if (StartsWith(bytes, bom))
                {
                    encoding = withBom;
                    return withBom.GetString(bytes, bom.Length, bytes.Length - bom.Length);
                }
            }

            try
            {
                // Strict UTF-8 over the whole file in one call, so any invalid bytes, including an
                // incomplete sequence at the end, throw instead of decoding to U+FFFD.
                encoding = new UTF8Encoding(false, true);
                return encoding.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                encoding = Encoding.Default;
                return encoding.GetString(bytes);
            }
        }

        private static readonly Encoding[] BomEncodings =
        {
            Encoding.UTF32, Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode, new UTF32Encoding(true, true)
        };

        private static bool StartsWith(byte[] bytes, byte[] prefix)
        {
            if (bytes.Length < prefix.Length)
                return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (bytes[i] != prefix[i])
                    return false;
            }
            return true;
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
