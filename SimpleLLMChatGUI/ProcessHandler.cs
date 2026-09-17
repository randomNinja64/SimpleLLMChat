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
        private StringBuilder textBuffer = new StringBuilder(); // Buffer for incomplete text

        public event Action<string> OutputReceived;
        public event Action<string> ErrorOccurred;
        public event Action GenerationComplete;
        public event Func<string, string, bool> ApprovalRequested;
        public event Action<int> StatusReceived;

        private readonly StringBuilder streamBuffer = new StringBuilder();
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
                streamBuffer.Clear();
                llmProcess.Start();

                statusPipeClient = new StatusPipeClient(llmProcess.Id);
                statusPipeClient.StatusReceived += OnStatusPipeReceived;
                statusPipeClient.IndexingStatusReceived += OnIndexingStatusPipeReceived;
                statusPipeClient.ReadyReceived += OnStatusPipeReady;
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

        private void ProcessStreamingText(string newText)
        {
            if (string.IsNullOrEmpty(newText))
                return;

            streamBuffer.Append(newText);

            while (true)
            {
                string buffered = streamBuffer.ToString();
                int promptIndex = buffered.IndexOf(ToolApproval.ApprovalPrompt, StringComparison.Ordinal);
                if (promptIndex < 0)
                {
                    FlushBufferedTextWithoutApprovalPrompt(buffered);
                    return;
                }

                int blockStart = buffered.LastIndexOf(ToolApproval.RunToolPrefix, promptIndex, StringComparison.Ordinal);
                if (blockStart < 0)
                {
                    ProcessTextChunk(buffered.Substring(0, promptIndex + ToolApproval.ApprovalPrompt.Length));
                    streamBuffer.Clear();
                    streamBuffer.Append(buffered.Substring(promptIndex + ToolApproval.ApprovalPrompt.Length));
                    continue;
                }

                if (blockStart > 0)
                    ProcessTextChunk(buffered.Substring(0, blockStart));

                string approvalBlock = buffered.Substring(blockStart, promptIndex + ToolApproval.ApprovalPrompt.Length - blockStart);
                HandleApprovalBlock(approvalBlock);

                string remaining = buffered.Substring(promptIndex + ToolApproval.ApprovalPrompt.Length);
                streamBuffer.Clear();
                if (string.IsNullOrEmpty(remaining))
                    return;

                streamBuffer.Append(remaining);
            }
        }

        private void FlushBufferedTextWithoutApprovalPrompt(string buffered)
        {
            int runIndex = buffered.LastIndexOf(ToolApproval.RunToolPrefix, StringComparison.Ordinal);
            if (runIndex >= 0)
            {
                if (runIndex > 0)
                    ProcessTextChunk(buffered.Substring(0, runIndex));

                streamBuffer.Clear();
                streamBuffer.Append(buffered.Substring(runIndex));
                return;
            }

            int holdBack = Math.Max(
                GetPartialSuffixLength(buffered, ToolApproval.ApprovalPrompt),
                GetPartialSuffixLength(buffered, ToolApproval.RunToolPrefix));

            if (holdBack > 0)
            {
                ProcessTextChunk(buffered.Substring(0, buffered.Length - holdBack));
                streamBuffer.Clear();
                streamBuffer.Append(buffered.Substring(buffered.Length - holdBack));
            }
            else
            {
                ProcessTextChunk(buffered);
                streamBuffer.Clear();
            }
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

        private void HandleApprovalBlock(string approvalBlock)
        {
            string toolName;
            string arguments;
            if (!ToolApproval.TryParseApprovalPrompt(approvalBlock, out toolName, out arguments))
            {
                ErrorOccurred?.Invoke("Failed to parse tool approval prompt.");
                SendApprovalResponse(false);
                return;
            }

            bool approved = false;
            if (ApprovalRequested != null)
                approved = ApprovalRequested(toolName, arguments);

            SendApprovalResponse(approved);
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
                // CLI still prints You: for TTY; strip it from the GUI transcript.
                string filteredText = Regex.Replace(
                    text,
                    @"(^|\r?\n)[ \t]*You:[ \t]*",
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
