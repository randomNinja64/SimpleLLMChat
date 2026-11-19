using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SimpleLLMChatGUI
{
    public class ProcessHandler : IDisposable
    {
        private Process llmProcess;
        private bool disposed;

        public event Action<string> OutputReceived;
        public event Action<ChatBlockKind, string> BlockStarted;
        public event Action BlockEnded;
        public event Action<string> ErrorOccurred;
        public event Action<string> CliErrorReceived;
        public event Action GenerationComplete;
        public event Func<string, string, string, bool> ApprovalRequested;
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
                string workingDirectory = Path.GetDirectoryName(executablePath);
                if (!string.IsNullOrEmpty(workingDirectory))
                    llmProcess.StartInfo.WorkingDirectory = workingDirectory;
                llmProcess.StartInfo.UseShellExecute = false;
                llmProcess.StartInfo.RedirectStandardOutput = true;
                llmProcess.StartInfo.RedirectStandardError = true;
                llmProcess.StartInfo.RedirectStandardInput = true;
                llmProcess.StartInfo.StandardErrorEncoding = Encoding.UTF8;
                llmProcess.StartInfo.CreateNoWindow = true;
                llmProcess.StartInfo.Arguments = "--no-banners";
                llmProcess.ErrorDataReceived += OnErrorDataReceived;
                llmProcess.Start();
                llmProcess.BeginErrorReadLine();

                statusPipeClient = new StatusPipeClient(llmProcess.Id);
                statusPipeClient.StatusReceived += OnStatusPipeReceived;
                statusPipeClient.IndexingStatusReceived += OnIndexingStatusPipeReceived;
                statusPipeClient.ReadyReceived += OnStatusPipeReady;
                statusPipeClient.ApprovalReceived += OnStatusPipeApproval;
                statusPipeClient.TextReceived += OutputText;
                statusPipeClient.BlockStarted += OnStatusPipeBlockStarted;
                statusPipeClient.BlockEnded += OnStatusPipeBlockEnded;
                statusPipeClient.Start();

                // Chat output arrives on the status pipe. Stdout is only drained
                // so the CLI never blocks on a full buffer.
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

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (disposed || string.IsNullOrEmpty(e.Data))
                return;

            Action<string> handler = CliErrorReceived;
            if (handler != null)
                handler(e.Data);
        }

        private void OnStatusPipeReceived(int tokens)
        {
            Action<int> handler = StatusReceived;
            if (handler != null)
                handler(tokens);
        }

        private void OnStatusPipeBlockStarted(ChatBlockKind kind, string name)
        {
            Action<ChatBlockKind, string> handler = BlockStarted;
            if (handler != null)
                handler(kind, name);
        }

        private void OnStatusPipeBlockEnded()
        {
            Action handler = BlockEnded;
            if (handler != null)
                handler();
        }

        private void OnStatusPipeReady()
        {
            Action handler = GenerationComplete;
            if (handler != null)
                handler();
        }

        private void OnStatusPipeApproval(string toolName, string arguments, string confidence)
        {
            bool approved = false;
            if (ApprovalRequested != null)
                approved = ApprovalRequested(toolName, arguments, confidence);

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
                statusPipeClient.TextReceived -= OutputText;
                statusPipeClient.BlockStarted -= OnStatusPipeBlockStarted;
                statusPipeClient.BlockEnded -= OnStatusPipeBlockEnded;
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

        /// <summary>Asks the CLI to stop the turn in progress.</summary>
        public void SendStop()
        {
            try
            {
                using (var stopEvent = System.Threading.EventWaitHandle.OpenExisting(
                    StatusPipe.GetCancelEventName(llmProcess.Id)))
                {
                    stopEvent.Set();
                }
            }
            catch
            {
                // CLI already gone; nothing to stop.
            }
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
                BeginReadOutput(stream, buffer);
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

        private void OutputText(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                string filteredText = NormalizeLineEndings(text);

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
            BlockStarted = null;
            BlockEnded = null;
            ErrorOccurred = null;
            CliErrorReceived = null;
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
