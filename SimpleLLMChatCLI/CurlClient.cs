using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Text;

namespace SimpleLLMChatCLI
{
    internal static class CurlClient
    {
        public static LLMClient.LLMCompletionResponse SendRequest(
            string serverUrl, string apiKey, JObject payload,
            Action<string> onReasoningChunk, Action<int> onReasoningSummary)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "curl.exe",
                    Arguments = "-s -N -X POST"
                        + " -H \"Content-Type: application/json\""
                        + " -H \"Authorization: Bearer " + apiKey + "\""
                        + " -d @-"
                        + " \"" + serverUrl + "/v1/chat/completions\"",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi))
                {
                    process.StandardInput.Write(payload.ToString(Formatting.None));
                    process.StandardInput.Close();

                    LLMClient.LLMCompletionResponse result = SseStreamParser.Parse(
                        process.StandardOutput, onReasoningChunk, onReasoningSummary);

                    process.WaitForExit();

                    if (process.ExitCode != 0 && string.IsNullOrEmpty(result.Content))
                    {
                        string stderr = process.StandardError.ReadToEnd();
                        return new LLMClient.LLMCompletionResponse(
                            "cURL failed (exit " + process.ExitCode + "): " + stderr,
                            null, "request_failed");
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                return new LLMClient.LLMCompletionResponse("cURL fallback failed: " + ex.Message, null, "request_failed");
            }
        }
    }
}
