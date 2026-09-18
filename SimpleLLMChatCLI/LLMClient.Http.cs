using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace SimpleLLMChatCLI
{
public partial class LLMClient
{
    LLMCompletionResponse sendMessages(
        List<ChatMessage> conversation,
        List<string> enabledTools,
        Action<string> outputCallback = null,
        Action<ToolRegistry.ToolCall> toolCallCallback = null,
        Action onContentStart = null,
        Action startBlock = null,
        bool outputOnly = false,
        List<string> contextInjections = null)
    {
        return SendHttpRequest(BuildRequestPayload(conversation, enabledTools, contextInjections), outputCallback, toolCallCallback, onContentStart, startBlock, outputOnly);
    }

    private LLMCompletionResponse SendHttpRequest(JObject payload, Action<string> outputCallback = null,
        Action<ToolRegistry.ToolCall> toolCallCallback = null,
        Action onContentStart = null, Action startBlock = null, bool outputOnly = false)
    {
        Action<string> onReasoningChunk = null;
        Action<int> onReasoningSummary = null;
        if (outputCallback != null && !outputOnly)
        {
            bool showThinking = config.GetChatBlockDisplayMode("thinkingdisplay", ChatBlockDisplayMode.Collapsed)
                != ChatBlockDisplayMode.Hidden;
            onReasoningChunk = showThinking ? outputCallback : null;
            if (!showThinking)
            {
                onReasoningSummary = s =>
                {
                    if (startBlock != null) startBlock();
                    outputCallback("[thought for " + s + " second" + (s == 1 ? "" : "s") + "]\n");
                };
            }
        }

        try
        {
            var request = (HttpWebRequest)WebRequest.Create($"{config.GetConfigValue("llmserver")}/v1/chat/completions");
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", "Bearer " + config.GetConfigValue("apikey"));

            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            request.ContentLength = payloadBytes.Length;

            using (var stream = request.GetRequestStream())
            {
                stream.Write(payloadBytes, 0, payloadBytes.Length);
            }

            using (var httpResponse = (HttpWebResponse)request.GetResponse())
            using (var responseStream = httpResponse.GetResponseStream())
            using (var reader = new StreamReader(responseStream, Encoding.UTF8))
            {
                return SseStreamParser.Parse(reader, outputCallback, onReasoningChunk, onReasoningSummary, toolCallCallback, onContentStart, startBlock);
            }
        }
        catch (Exception ex)
        {
            string reason;
            WebException webEx = ex as WebException;
            if (webEx != null && webEx.Response is HttpWebResponse errorResponse)
            {
                using (var errorStream = errorResponse.GetResponseStream())
                using (var errorReader = new StreamReader(errorStream, Encoding.UTF8))
                {
                    string body = errorReader.ReadToEnd();
                    reason = "HTTP " + (int)errorResponse.StatusCode + " " + errorResponse.StatusDescription + ": " + body;
                }
            }
            else
            {
                reason = ex.Message;
            }

            string serverUrl = config.GetConfigValue("llmserver") ?? "";
            if (TlsCurlFallback.CanAttempt(serverUrl, ex))
                return CurlClient.SendRequest(serverUrl, config.GetConfigValue("apikey"), payload, outputCallback, onReasoningChunk, onReasoningSummary, toolCallCallback, onContentStart, startBlock);

            return new LLMCompletionResponse(reason, null, "request_failed");
        }
    }

    private static bool CliRequestApproval(string toolName, string arguments)
    {
        ChatOutput.WriteLine(ToolApproval.FormatApprovalMessage(toolName, arguments));
        Console.Out.Flush();

        if (Program.StatusPipe != null)
            Program.StatusPipe.PublishDiscrete(StatusPipe.FormatApproval(toolName, arguments));

        while (true)
        {
            ChatOutput.Write(ToolApproval.ApprovalPrompt);
            Console.Out.Flush();

            string input = Console.ReadLine();
            ChatOutput.EndInputLine();
            if (input == null)
                continue;

            input = input.Trim();
            if (input.Length == 0)
                continue;

            if (string.Equals(input, "Y", StringComparison.OrdinalIgnoreCase)
                || string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(input, "N", StringComparison.OrdinalIgnoreCase)
                || string.Equals(input, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            ChatOutput.WriteLine("Please enter Y or N.");
        }
    }
}
}
