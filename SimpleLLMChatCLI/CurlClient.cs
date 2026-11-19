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
            string serverUrl, string apiKey, JObject payload, StreamSink sink)
        {
            string url = serverUrl.TrimEnd('/') + "/chat/completions";
            try
            {
                LLMClient.LLMCompletionResponse result = new LLMClient.LLMCompletionResponse(
                    string.Empty, null, string.Empty);
                string stderr = null;
                bool parsed = false;
                byte[] body = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
                int exitCode = CurlHttpsClient.Post(url, apiKey, body, noBuffer: true, process =>
                {
                    Cancel.OnAbort(process.Kill);
                    Thread errThread = new Thread(() =>
                    {
                        try { stderr = process.StandardError.ReadToEnd(); }
                        catch { }
                    });
                    errThread.IsBackground = true;
                    errThread.Start();
                    result = new SseStreamParser(sink).Read(process.StandardOutput);
                    parsed = true;
                    errThread.Join(5000);
                });

                if (Cancel.Requested)
                    return result;

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
