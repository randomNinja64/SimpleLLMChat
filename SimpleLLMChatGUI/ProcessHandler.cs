using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SimpleLLMChatGUI
{
    public class ProcessHandler : IDisposable
    {
        private Process llmProcess;
        private bool disposed;
        private StringBuilder textBuffer = new StringBuilder(); // Buffer for incomplete You: strip

        public event Action<string> OutputReceived;
        public event Action<string> ErrorOccurred;
        public event Action GenerationComplete;
        public event Func<string, string, bool> ApprovalRequested;
        public event Action<int> StatusReceived;

        private StatusPipeClient statusPipeClient;

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
                DisposeStatusPipeClient();

                llmProcess = new Process();
                llmProcess.StartInfo.FileName = executablePath;
                llmProcess.StartInfo.UseShellExecute = false;
                llmProcess.StartInfo.RedirectStandardOutput = true;
                llmProcess.StartInfo.RedirectStandardInput = true;
                llmProcess.StartInfo.CreateNoWindow = true;
                llmProcess.StartInfo.Arguments = "--no-banners";
                textBuffer.Clear();
                llmProcess.Start();

                statusPipeClient = new StatusPipeClient(llmProcess.Id);
                statusPipeClient.StatusReceived += OnStatusPipeReceived;
                statusPipeClient.IndexingStatusReceived += OnIndexingStatusPipeReceived;
                statusPipeClient.ReadyReceived += OnStatusPipeReady;
                statusPipeClient.ApprovalReceived += OnStatusPipeApproval;
                statusPipeClient.Start();

                // 256 byte async buffer
                var buffer = new byte[256];
                Stream outputStream = llmProcess.StandardOutput.BaseStream;
                BeginReadOutput(outputStream, buffer);

                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Failed to start process: " + ex.Message);
                return false;
            }
        }

        private void OnStatusPipeReceived(int tokens)
        {
            Action<int> handler = StatusReceived;
            if (handler != null)
                handler(tokens);
        }

        private void OnStatusPipeReady()
        {
            Action handler = GenerationComplete;
            if (handler != null)
                handler();
        }

        private void OnStatusPipeApproval(string toolName, string arguments)
        {
            bool approved = false;
            if (ApprovalRequested != null)
                approved = ApprovalRequested(toolName, arguments);

            SendApprovalResponse(approved);
        }

        private void OnIndexingStatusPipeReceived(IndexingStatusEvent status)
        {
            IndexingStatusHub.Publish(status);
        }

        private void DisposeStatusPipeClient()
        {
            if (statusPipeClient != null)
            {
                statusPipeClient.StatusReceived -= OnStatusPipeReceived;
                statusPipeClient.IndexingStatusReceived -= OnIndexingStatusPipeReceived;
                statusPipeClient.ReadyReceived -= OnStatusPipeReady;
                statusPipeClient.ApprovalReceived -= OnStatusPipeApproval;
                statusPipeClient.Dispose();
                statusPipeClient = null;
            }
        }

        public bool SendInput(string input)
        {
            if (llmProcess != null && !llmProcess.HasExited)
            {
                try
                {
                    // Encode multi-line text as single line: replace newlines with <<NEWLINE>> marker
                    string encodedInput = input.Replace("\r\n", "<<NEWLINE>>").Replace("\n", "<<NEWLINE>>").Replace("\r", "<<NEWLINE>>");
                    llmProcess.StandardInput.WriteLine(encodedInput);
                    llmProcess.StandardInput.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke("Error sending input: " + ex.Message);
                    return false;
                }
            }
            return false;
        }

        public bool SendInputWithImage(string imagePath, string prompt)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                ErrorOccurred?.Invoke("Image path cannot be empty.");
                return false;
            }

            // Ensure the path is quoted
            string quotedPath = "\"" + imagePath + "\"";

            // Build the final command
            string command = "/image " + quotedPath + " " + prompt;

            return SendInput(command);
        }

        public bool SendReasoningEffort(string effort)
        {
            string command = string.IsNullOrEmpty(effort) ? "/reasoning" : "/reasoning " + effort;
            return SendInput(command);
        }

        public bool SendReload()
        {
            return SendInput("/reload");
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
                        ProcessTextChunk(newText);
                }
                catch
                {
                    // If UTF-8 decoding fails, skip this chunk
                }

                // Continue reading
                BeginReadOutput(stream, buffer);
            }
        }

        public bool SendApprovalResponse(bool approved)
        {
            if (llmProcess != null && !llmProcess.HasExited)
            {
                try
                {
                    llmProcess.StandardInput.WriteLine(approved ? "Y" : "N");
                    llmProcess.StandardInput.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke("Error sending approval response: " + ex.Message);
                    return false;
                }
            }

            return false;
        }

        private static int GetPartialSuffixLength(string text, string marker)
        {
            int maxLength = Math.Min(marker.Length - 1, text.Length);
            for (int length = maxLength; length > 0; length--)
            {
                if (text.EndsWith(marker.Substring(0, length), StringComparison.Ordinal))
                    return length;
            }

            return 0;
        }

        private void ProcessTextChunk(string textChunk)
        {
            if (string.IsNullOrEmpty(textChunk))
                return;

            if (textBuffer.Length > 0)
            {
                textBuffer.Append(textChunk);
                textChunk = textBuffer.ToString();
                textBuffer.Clear();
            }

            // Hold back a trailing partial "You:" so the CLI prompt is not split
            // across chunks before OutputText can strip it. Turn-end is STATUS ready.
            int holdBack = GetPartialSuffixLength(textChunk, "You:");
            if (holdBack > 0)
            {
                if (holdBack < textChunk.Length)
                    OutputText(textChunk.Substring(0, textChunk.Length - holdBack));
                textBuffer.Append(textChunk.Substring(textChunk.Length - holdBack));
                return;
            }

            OutputText(textChunk);
        }

        private void OutputText(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                // CLI still prints You: / Approve? for TTY; strip from the GUI transcript.
                // Approval interaction is STATUS approval on the control pipe.
                string filteredText = Regex.Replace(
                    text,
                    @"(^|\r?\n)[ \t]*You:[ \t]*",
                    match => match.Groups[1].Value,
                    RegexOptions.Multiline
                );
                filteredText = Regex.Replace(
                    filteredText,
                    @"(^|\r?\n)[ \t]*" + Regex.Escape(ToolApproval.ApprovalPrompt),
                    match => match.Groups[1].Value,
                    RegexOptions.Multiline
                );

                filteredText = NormalizeLineEndings(filteredText);

                if (!string.IsNullOrEmpty(filteredText))
                {
                    OutputReceived?.Invoke(filteredText);
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
            if (disposed)
                return;

            disposed = true;
            DisposeStatusPipeClient();
            OutputReceived = null;
            ErrorOccurred = null;
            GenerationComplete = null;
            ApprovalRequested = null;
            StatusReceived = null;
            if (llmProcess != null && !llmProcess.HasExited)
            {
                try { llmProcess.Kill(); }
                catch { }
            }
            if (llmProcess != null)
            {
                llmProcess.Dispose();
                llmProcess = null;
            }
        }
    }
}
