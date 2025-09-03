using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace SimpleLLMChatGUI
{
    public class ProcessHandler : IDisposable
    {
        private Process llmProcess;
        private const int skipInitialLines = 4; // CLI banner lines
        private StringBuilder textBuffer = new StringBuilder(); // Buffer for incomplete text
        private bool initialLinesSkipped = false;
        private bool suppressOutput = false; // Flag to suppress output after clear command

        public event Action<string> OutputReceived;
        public event Action<string> ErrorOccurred;

        public bool IsProcessRunning
        {
            get { return llmProcess != null && !llmProcess.HasExited; }
        }

        public ProcessHandler()
        {
        }

        public bool StartProcess(string executablePath)
        {
            try
            {
                llmProcess = new Process();
                llmProcess.StartInfo.FileName = executablePath;
                llmProcess.StartInfo.UseShellExecute = false;
                llmProcess.StartInfo.RedirectStandardOutput = true;
                llmProcess.StartInfo.RedirectStandardInput = true;
                llmProcess.StartInfo.CreateNoWindow = true;

                llmProcess.Start();

                // 256 byte async buffer
                var buffer = new byte[256];
                Stream outputStream = llmProcess.StandardOutput.BaseStream;
                BeginReadOutput(outputStream, buffer);

                return true;
            }
            catch (Exception ex)
            {
                if (ErrorOccurred != null)
                {
                    ErrorOccurred("Failed to start process: " + ex.Message);
                }
                return false;
            }
        }

        public bool SendInput(string input)
        {
            if (llmProcess != null && !llmProcess.HasExited)
            {
                try
                {
                    // Check if this is a clear command
                    if (input.Trim().ToLower() == "clear")
                    {
                        suppressOutput = true;
                    }

                    llmProcess.StandardInput.WriteLine(input);
                    llmProcess.StandardInput.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    if (ErrorOccurred != null)
                    {
                        ErrorOccurred("Error sending input: " + ex.Message);
                    }
                    return false;
                }
            }
            return false;
        }

        public bool SendInputWithImage(string imagePath, string prompt)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                if (ErrorOccurred != null)
                    ErrorOccurred("Image path cannot be empty.");
                return false;
            }

            // Ensure the path is quoted
            string quotedPath = "\"" + imagePath + "\"";

            // Build the final command
            string command = "image " + quotedPath + " " + prompt;

            // Debug: show exactly what will be sent
            //MessageBox.Show("Sending command:\n" + command, "Debug Input");

            return SendInput(command);
        }

        private void BeginReadOutput(Stream stream, byte[] buffer)
        {
            try
            {
                stream.BeginRead(buffer, 0, buffer.Length, new AsyncCallback(OutputReadCallback), new Tuple<Stream, byte[]>(stream, buffer));
            }
            catch
            {
                // ignore errors
            }
        }

        private void OutputReadCallback(IAsyncResult ar)
        {
            var state = (Tuple<Stream, byte[]>)ar.AsyncState;
            Stream stream = state.Item1;
            byte[] buffer = state.Item2;

            int bytesRead;
            try
            {
                bytesRead = stream.EndRead(ar);
            }
            catch
            {
                return;
            }

            if (bytesRead > 0)
            {
                try
                {
                    string newText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (!string.IsNullOrEmpty(newText))
                    {
                        // Process text immediately for streaming
                        ProcessStreamingText(newText);
                    }
                }
                catch
                {
                    // If UTF-8 decoding fails, skip this chunk
                }

                // Continue reading
                BeginReadOutput(stream, buffer);
            }
        }

        private void ProcessStreamingText(string newText)
        {
            // Skip initial banner lines if we haven't already
            if (!initialLinesSkipped)
            {
                textBuffer.Append(newText);
                string bufferedText = textBuffer.ToString();

                // Count lines in the buffer
                int newlineCount = 0;
                foreach (char c in bufferedText)
                {
                    if (c == '\n') newlineCount++;
                }

                if (newlineCount >= skipInitialLines)
                {
                    // Find where to start after skipping initial lines
                    int skipIndex = 0;
                    int linesSkipped = 0;

                    for (int i = 0; i < bufferedText.Length && linesSkipped < skipInitialLines; i++)
                    {
                        if (bufferedText[i] == '\n')
                        {
                            linesSkipped++;
                            if (linesSkipped == skipInitialLines)
                            {
                                skipIndex = i + 1;
                                break;
                            }
                        }
                    }

                    string remainingText = bufferedText.Substring(skipIndex);
                    // Clean up any leading whitespace/newlines after skipping banner
                    remainingText = remainingText.TrimStart('\r', '\n', ' ', '\t');
                    initialLinesSkipped = true;
                    textBuffer.Clear();

                    // Process any remaining text after banner
                    if (!string.IsNullOrEmpty(remainingText))
                    {
                        ProcessTextChunk(remainingText);
                    }
                }
                else
                {
                    // Still accumulating initial lines to skip
                    return;
                }
            }
            else
            {
                // Banner already skipped, process text immediately
                ProcessTextChunk(newText);
            }
        }

        private void ProcessTextChunk(string textChunk)
        {
            // For streaming, we want to output text immediately
            // But we need to be careful about "You:" patterns

            // Check if this chunk contains a "You:" pattern
            int youIndex = textChunk.IndexOf("You:");
            if (youIndex >= 0)
            {
                // Check if this "You:" is complete (followed by whitespace or newline)
                bool isCompletePattern = false;
                if (youIndex + 4 < textChunk.Length)
                {
                    char nextChar = textChunk[youIndex + 4];
                    isCompletePattern = char.IsWhiteSpace(nextChar) || nextChar == '\n' || nextChar == '\r';
                }
                else
                {
                    // "You:" is at the end, so it's incomplete
                    isCompletePattern = false;
                }

                if (isCompletePattern)
                {
                    // Process everything before "You:" and output it
                    string textBeforeYou = textChunk.Substring(0, youIndex);
                    if (!string.IsNullOrEmpty(textBeforeYou))
                    {
                        OutputText(textBeforeYou);
                    }

                    // Don't output the "You:" pattern itself
                    // Keep any text after "You:" for next chunk
                    string textAfterYou = textChunk.Substring(youIndex + 4);
                    if (!string.IsNullOrEmpty(textAfterYou))
                    {
                        // Remove leading whitespace after "You:"
                        textAfterYou = textAfterYou.TrimStart(' ', '\t');
                        if (!string.IsNullOrEmpty(textAfterYou))
                        {
                            OutputText(textAfterYou);
                        }
                    }
                }
                else
                {
                    // Incomplete "You:" pattern, output everything except the last few characters
                    int safeEndIndex = Math.Max(0, youIndex - 1);
                    string safeText = textChunk.Substring(0, safeEndIndex);
                    if (!string.IsNullOrEmpty(safeText))
                    {
                        OutputText(safeText);
                    }

                    // Keep the incomplete pattern for next chunk
                    textBuffer.Append(textChunk.Substring(safeEndIndex));
                }
            }
            else
            {
                // No "You:" pattern in this chunk, output it immediately
                OutputText(textChunk);
            }
        }

        private void OutputText(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                // If we're suppressing output after a clear command, don't output anything
                if (suppressOutput)
                {
                    suppressOutput = false; // Reset the flag after first output
                    return;
                }

                // Remove any "You:" prompts from the middle of the text
                string filteredText = Regex.Replace(
                    text,
                    @"(^|\r?\n)[ \t]*You:[ \t]*",
                    match => match.Groups[1].Value, // Keep just the newline part
                    RegexOptions.Multiline
                );

                // Normalize line endings for Windows display
                filteredText = NormalizeLineEndings(filteredText);

                // Raise event immediately for real-time streaming
                if (!string.IsNullOrEmpty(filteredText) && OutputReceived != null)
                {
                    OutputReceived(filteredText);
                }
            }
        }

        private string NormalizeLineEndings(string text)
        {
            // First normalize all line endings to \n
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            // Then convert to Windows standard \r\n
            return text.Replace("\n", "\r\n");
        }

        public void Dispose()
        {
            if (llmProcess != null && !llmProcess.HasExited)
            {
                try { llmProcess.Kill(); }
                catch { }
                llmProcess.Dispose();
            }
        }

        public void RestartProcess()
        {
            Dispose();
            StartProcess("SimpleLLMChatCLI.exe");
        }
    }
}
