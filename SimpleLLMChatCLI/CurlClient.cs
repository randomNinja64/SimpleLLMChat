using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace SimpleLLMChatCLI
{
    internal static class CurlClient
    {
        public static LLMClient.LLMCompletionResponse SendRequest(
            string serverUrl, string apiKey, JObject payload,
            Action<string> outputCallback,
            Action<string> onReasoningChunk,
            Action<int> onReasoningSummary,
            Action onContentStart = null,
            Action startBlock = null)
        {
            // Use a Windows named pipe so curl can read the JSON body as a file
            // from memory without touching disk, avoiding stdin pipe deadlock issues.
            string pipeName = "llmchat_" + Guid.NewGuid().ToString("N");
            byte[] jsonBytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));

            try
            {
                using (NamedPipeServerStream pipeServer = new NamedPipeServerStream(
                    pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte))
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "curl.exe",
                        Arguments = "-s -N -X POST"
                            + " -H \"Content-Type: application/json\""
                            + " -H \"Authorization: Bearer " + apiKey + "\""
                            + " --data-binary \"@\\\\.\\pipe\\" + pipeName + "\""
                            + " \"" + serverUrl + "/v1/chat/completions\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    using (Process process = Process.Start(psi))
                    {
                        // Serve the JSON body via the named pipe on a background thread
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

                        LLMClient.LLMCompletionResponse result = SseStreamParser.Parse(
                            process.StandardOutput, outputCallback, onReasoningChunk, onReasoningSummary, onContentStart, startBlock);

                        pipeThread.Join(5000);
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            string stderr = process.StandardError.ReadToEnd();
                            return new LLMClient.LLMCompletionResponse(
                                "cURL failed (exit " + process.ExitCode + "): " + stderr,
                                null, "request_failed");
                        }

                        if (string.IsNullOrEmpty(result.Content) && string.IsNullOrEmpty(result.FinishReason))
                        {
                            string stderr = process.StandardError.ReadToEnd();
                            string detail = string.IsNullOrEmpty(stderr) ? "no response data" : stderr.Trim();
                            return new LLMClient.LLMCompletionResponse(
                                "cURL returned no response: " + detail,
                                null, "request_failed");
                        }

                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return new LLMClient.LLMCompletionResponse("cURL fallback failed: " + ex.Message, null, "request_failed");
            }
        }
    }
}
