using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace SimpleLLMChatCLI
{
public partial class LLMClient
{
    private LLMCompletionResponse SendMessages(
        List<ChatMessage> conversation,
        List<string> enabledTools,
        StreamSink sink,
        List<string> contextInjections = null)
    {
        return SendHttpRequest(BuildRequestPayload(conversation, enabledTools, contextInjections), sink);
    }

    private LLMCompletionResponse SendHttpRequest(JObject payload, StreamSink sink)
    {
        try
        {
            var request = (HttpWebRequest)WebRequest.Create(config.GetConfigValue("llmserver").Trim().TrimEnd('/') + "/chat/completions");
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", "Bearer " + config.GetConfigValue("apikey"));

            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            request.ContentLength = payloadBytes.Length;
            Cancel.OnAbort(request.Abort);

            using (var stream = request.GetRequestStream())
            {
                stream.Write(payloadBytes, 0, payloadBytes.Length);
            }

            using (var httpResponse = (HttpWebResponse)request.GetResponse())
            using (var responseStream = httpResponse.GetResponseStream())
            using (var reader = new StreamReader(responseStream, Encoding.UTF8))
            {
                return new SseStreamParser(sink).Read(reader);
            }
        }
        catch (Exception ex)
        {
            if (Cancel.Requested)
                return new LLMCompletionResponse(string.Empty, null, string.Empty);

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
                return CurlClient.SendRequest(serverUrl, config.GetConfigValue("apikey"), payload, sink);

            return new LLMCompletionResponse(reason, null, "request_failed");
        }
    }

    private static bool CliRequestApproval(string toolName, string arguments, double? confidence)
    {
        if (confidence.HasValue)
        {
            string confidenceText = confidence.Value.ToString("0.00", CultureInfo.InvariantCulture);
            ChatOutput.WriteLine(ToolApproval.FormatApprovalMessage(toolName, arguments, confidenceText));
            if (Program.Pipe != null)
                Program.Pipe.PublishDiscrete(ChatPipe.FormatApproval(toolName, arguments, confidence.Value));
        }
        else
        {
            ChatOutput.WriteLine(ToolApproval.FormatApprovalMessage(toolName, arguments));
            if (Program.Pipe != null)
                Program.Pipe.PublishDiscrete(ChatPipe.FormatApproval(toolName, arguments));
        }
        Console.Out.Flush();

        while (true)
        {
            ChatOutput.Write(ToolApproval.ApprovalPrompt, false);
            Console.Out.Flush();

            string input = Console.ReadLine();
            ChatOutput.EndInputLine();
            if (input == null)
            {
                // Ctrl+C ends ReadLine before its handler has run; give it a moment.
                // Otherwise stdin is closed and no answer can arrive, so deny.
                System.Threading.Thread.Sleep(100);
                return false;
            }

            input = input.Trim();
            if (input.Length == 0)
                continue;

            // A command sent while the turn was running (the GUI's /save, /reload,
            // /reasoning) is not an answer: keep it for the main loop.
            if (input.StartsWith("/"))
            {
                Program.DeferredInput.Enqueue(input);
                continue;
            }

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
