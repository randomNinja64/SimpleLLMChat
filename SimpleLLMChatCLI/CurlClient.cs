using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace SimpleLLMChatCLI
{
    /// <summary>
    /// curl.exe HTTPS fallback via a Windows named pipe (body never touches disk).
    /// Chat completions stream SSE (<see cref="SendRequest"/>); embeddings use a
    /// buffered JSON response (<see cref="PostJson"/>).
    /// </summary>
    internal static class CurlClient
    {
        public static bool CanFallback(string url, Exception ex)
        {
            return TlsCurlFallback.CanAttempt(url, ex);
        }

        /// <summary>
        /// Non-streaming JSON POST. Used for embeddings (and similar one-shot APIs).
        /// </summary>
        public static string PostJson(string fullUrl, string apiKey, JObject payload, out int exitCode)
        {
            exitCode = -1;
            try
            {
                string output = null;
                string error = null;
                exitCode = RunPost(fullUrl, apiKey, payload, noBuffer: false, process =>
                {
                    Thread errThread = StartStderrReader(process, text => error = text);
                    output = process.StandardOutput.ReadToEnd();
                    errThread.Join(5000);
                });

                if (exitCode != 0 && string.IsNullOrEmpty(output))
                    return "cURL failed (exit " + exitCode + "): " + error;
                return output;
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "cURL fallback failed: " + ex.Message;
            }
        }

        /// <summary>
        /// Streaming chat-completions POST. Parses SSE from curl stdout.
        /// </summary>
        public static LLMClient.LLMCompletionResponse SendRequest(
            string serverUrl, string apiKey, JObject payload,
            Action<string> outputCallback,
            Action<string> onReasoningChunk,
            Action<int> onReasoningSummary,
            Action<ToolRegistry.ToolCall> onToolCallChunk = null,
            Action onContentStart = null,
            Action startBlock = null)
        {
            string url = serverUrl.TrimEnd('/') + "/v1/chat/completions";
            try
            {
                LLMClient.LLMCompletionResponse result = new LLMClient.LLMCompletionResponse(
                    string.Empty, null, string.Empty);
                string stderr = null;
                bool parsed = false;
                int exitCode = RunPost(url, apiKey, payload, noBuffer: true, process =>
                {
                    Thread errThread = StartStderrReader(process, text => stderr = text);
                    result = SseStreamParser.Parse(
                        process.StandardOutput, outputCallback, onReasoningChunk, onReasoningSummary,
                        onToolCallChunk, onContentStart, startBlock);
                    parsed = true;
                    errThread.Join(5000);
                });

                if (exitCode != 0)
                {
                    return new LLMClient.LLMCompletionResponse(
                        "cURL failed (exit " + exitCode + "): " + stderr,
                        null, "request_failed");
                }

                if (!parsed
                    || (string.IsNullOrEmpty(result.Content) && string.IsNullOrEmpty(result.FinishReason)))
                {
                    string detail = string.IsNullOrEmpty(stderr) ? "no response data" : stderr.Trim();
                    return new LLMClient.LLMCompletionResponse(
                        "cURL returned no response: " + detail,
                        null, "request_failed");
                }

                return result;
            }
            catch (Exception ex)
            {
                return new LLMClient.LLMCompletionResponse(
                    "cURL fallback failed: " + ex.Message, null, "request_failed");
            }
        }

        private static Thread StartStderrReader(Process process, Action<string> onComplete)
        {
            Thread errThread = new Thread(() =>
            {
                string text = null;
                try { text = process.StandardError.ReadToEnd(); }
                catch { }
                if (onComplete != null)
                    onComplete(text);
            });
            errThread.IsBackground = true;
            errThread.Start();
            return errThread;
        }

        /// <summary>
        /// Shared pipe + process setup. <paramref name="consume"/> reads stdout while curl runs;
        /// the pipe writer is joined and the process waited on afterward. Returns exit code.
        /// </summary>
        private static int RunPost(string fullUrl, string apiKey, JObject payload, bool noBuffer, Action<Process> consume)
        {
            string pipeName = "llmcurl_" + Guid.NewGuid().ToString("N");
            byte[] jsonBytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));

            using (NamedPipeServerStream pipeServer = new NamedPipeServerStream(
                pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte))
            {
                string args = "-s"
                    + (noBuffer ? " -N" : "")
                    + " -X POST"
                    + " -H \"Content-Type: application/json\""
                    + (string.IsNullOrEmpty(apiKey) ? "" : " -H \"Authorization: Bearer " + apiKey + "\"")
                    + " --data-binary \"@\\\\.\\pipe\\" + pipeName + "\""
                    + " \"" + fullUrl + "\"";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = TlsCurlFallback.DefaultCurlPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process process = Process.Start(psi))
                {
                    Thread pipeThread = new Thread(() =>
                    {
                        try
                        {
                            pipeServer.WaitForConnection();
                            pipeServer.Write(jsonBytes, 0, jsonBytes.Length);
                            pipeServer.Flush();
                            pipeServer.Close();
                        }
                        catch { }
                    });
                    pipeThread.IsBackground = true;
                    pipeThread.Start();

                    consume(process);

                    pipeThread.Join(5000);
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
        }
    }
}
