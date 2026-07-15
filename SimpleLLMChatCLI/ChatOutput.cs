using System;
using System.Text;

namespace SimpleLLMChatCLI
{
    /// <summary>
    /// Wraps console chat output and tracks how many consecutive newlines were
    /// emitted last, so StartBlock can pad to exactly one blank line between
    /// output blocks regardless of how the previous block ended.
    /// Ported from NyoCoder's ChatOutputWriter.
    /// </summary>
    internal static class ChatOutput
    {
        // Start of an empty console counts as already separated.
        private static int _trailingNewlines = 2;

        // True once a visible (non-whitespace) character was written on the current line.
        private static bool _lineHasText;

        // Whitespace at the start of a line is held back until the line proves to
        // have visible text (preserving indentation); if the line ends blank, the
        // whitespace is dropped so whitespace-only lines render as empty lines.
        private static readonly StringBuilder _pendingIndent = new StringBuilder();

        /// <summary>
        /// Writes text to the console, capping consecutive blank lines at one.
        /// Lines containing only whitespace count as blank. This keeps blocks
        /// separated by exactly one blank line even when the model streams
        /// extra trailing newlines, and keeps StartBlock's tracking accurate.
        /// All chat output must flow through here.
        /// </summary>
        public static void Write(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            StringBuilder sb = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    continue; // fold CRLF into the LF that follows

                if (c == '\n')
                {
                    _pendingIndent.Length = 0; // drop whitespace on a blank line

                    // A line with no visible text extends the blank run; cap the
                    // run at one blank line (two consecutive newlines).
                    int run = _lineHasText ? 0 : _trailingNewlines;
                    if (run < 2)
                    {
                        sb.Append('\n');
                        _trailingNewlines = run + 1;
                    }
                    _lineHasText = false;
                }
                else if (!_lineHasText && (c == ' ' || c == '\t' || c == '\r'))
                {
                    _pendingIndent.Append(c);
                }
                else
                {
                    if (_pendingIndent.Length > 0)
                    {
                        sb.Append(_pendingIndent);
                        _pendingIndent.Length = 0;
                    }
                    sb.Append(c);
                    if (c != ' ' && c != '\t' && c != '\r')
                    {
                        _lineHasText = true;
                        _trailingNewlines = 0;
                    }
                }
            }

            if (sb.Length > 0)
                Console.Write(sb.ToString());
        }

        public static void WriteLine(string text)
        {
            Write(text + "\n");
        }

        public static void WriteLine()
        {
            Write("\n");
        }

        /// <summary>
        /// Ensures the output ends with exactly one blank line before a new block starts.
        /// Emits 0, 1, or 2 newlines depending on what was written last.
        /// </summary>
        public static void StartBlock()
        {
            if (_trailingNewlines < 2)
                Write(new string('\n', 2 - _trailingNewlines));
        }

        /// <summary>
        /// Terminates the current line if the last write didn't end with a newline.
        /// </summary>
        public static void EndLine()
        {
            if (_trailingNewlines == 0)
                Write("\n");
        }

        /// <summary>
        /// Treats the output as already block-separated so the next StartBlock is a
        /// no-op. Used after silent command turns (e.g. /reasoning) so the re-prompt
        /// doesn't emit a stray newline — the GUI would show it as a blank line.
        /// </summary>
        public static void MarkSeparated()
        {
            _trailingNewlines = 2;
            _lineHasText = false;
            _pendingIndent.Length = 0;
        }

        /// <summary>
        /// Call after Console.ReadLine on a prompt line: the user's Enter (interactive)
        /// or the GUI's own echo of the input ends the line without going through Write.
        /// </summary>
        public static void EndInputLine()
        {
            if (_trailingNewlines == 0)
                _trailingNewlines = 1;
            _lineHasText = false;
        }
    }
}
