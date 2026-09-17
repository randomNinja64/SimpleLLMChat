using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Threading;

namespace SimpleLLMChatCLI
{
    /// <summary>
    /// Chat-completions curl fallback: SSE parse over <see cref="CurlHttpsClient.Post"/>.
    /// </summary>
    internal static class CurlClient
    {
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
                byte[] body = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
                int exitCode = CurlHttpsClient.Post(url, apiKey, body, noBuffer: true, process =>
                {
                    Thread errThread = new Thread(() =>
                    {
                        try { stderr = process.StandardError.ReadToEnd(); }
                        catch { }
                    });
                    errThread.IsBackground = true;
                    errThread.Start();
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
    }
}
