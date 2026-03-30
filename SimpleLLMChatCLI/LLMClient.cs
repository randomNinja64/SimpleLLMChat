using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace SimpleLLMChatCLI
{
public class LLMClient
{
    private readonly ConfigHandler config;
    private readonly ToolRegistry registry;

    public LLMClient(ConfigHandler config, ToolRegistry registry)
    {
        this.registry = registry;
        this.config = config;
        // Enable modern TLS protocols for HTTPS support
        // .NET 4.0 only has named constant for Tls (1.0)
        // Tls11 = 768, Tls12 = 3072, Tls13 = 12288 (numeric values used until .NET 4.5+)
        // We use |= to ADD to existing protocols rather than replacing them
        // This ensures fallback to older protocols if newer ones aren't available
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls | (SecurityProtocolType)768 | (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
        }
        catch
        {
            // If setting TLS protocols fails, continue with system defaults
            // This can happen on very old systems without TLS 1.2 support
        }
    }

    // Struct for chat messages
    public struct ChatMessage
    {
        public string Role;
        public string Content;
        public string Image;
        public List<ToolRegistry.ToolCall> ToolCalls;
        public string ToolCallId;

        public ChatMessage(string role, string content, string toolCallId = "")
        {
            Role = role;
            Content = content;
            ToolCallId = toolCallId;
            Image = null;
            ToolCalls = new List<ToolRegistry.ToolCall>();
        }
    }

    public struct LLMCompletionResponse
    {
        public string Content;
        public List<ToolRegistry.ToolCall> ToolCalls;
        public string FinishReason;
        public int ReasoningSeconds;

        public LLMCompletionResponse(string content, List<ToolRegistry.ToolCall> toolCalls, string finishReason)
        {
            Content = content;
            ToolCalls = toolCalls ?? new List<ToolRegistry.ToolCall>();
            FinishReason = finishReason;
            ReasoningSeconds = 0;
        }
    }

    public void ProcessConversation(
        List<ChatMessage> conversation,
        string userMessage,
        string image,
        string assistantName,
        List<string> enabledTools,
        List<string> toolsRequiringApproval,
        bool outputOnly,
        bool showToolOutput)
    {
        // Add user message
        ChatMessage userMsg = new ChatMessage
        {
            Role = "user",
            Content = userMessage,
            Image = image
        };
        conversation.Add(userMsg);

        while (true)
        {
            if (!outputOnly)
            {
                Console.WriteLine();
                Console.Write(assistantName + ": ");
            }

            Action<string> onReasoningChunk = (!outputOnly && config.GetConfigBool("showreasoningoutput")) ? (Action<string>)Console.Write : null;
            Action<int> onReasoningSummary = (!outputOnly && !config.GetConfigBool("showreasoningoutput")) ?
                (Action<int>)(s => Console.WriteLine("[thought for " + s + " second" + (s == 1 ? "" : "s") + "]")) : null;
            LLMCompletionResponse response = sendMessages(conversation, enabledTools, onReasoningChunk, onReasoningSummary);

            if (response.FinishReason == "request_failed")
            {
                Console.WriteLine("Request to LLM Failed (" + response.Content + ")");
                break;
            }

            if (response.ToolCalls != null && response.ToolCalls.Count > 0)
            {
                // Add assistant tool call message
                ChatMessage assistantCall = new ChatMessage
                {
                    Role = "assistant",
                    Content = string.Empty,
                    ToolCalls = response.ToolCalls
                };
                conversation.Add(assistantCall);

                for (int i = 0; i < response.ToolCalls.Count; i++)
                {
                    ToolRegistry.ToolCall call = response.ToolCalls[i];

                    if (!outputOnly)
                    {
                        Console.WriteLine("\n[tool request] " + call.Name + " with arguments: " + call.Arguments);
                    }

                    int exitCode = 0;
                    string toolContent;

                    if (!enabledTools.Contains(call.Name))
                    {
                        exitCode = -1;
                        toolContent = ToolRegistry.FormatCommandResult(
                            call.Name,
                            "error: tool '" + call.Name + "' is disabled by configuration.",
                            exitCode
                        );
                    }
                    else if (toolsRequiringApproval != null && toolsRequiringApproval.Contains(call.Name))
                    {
                        // Tool requires approval - prompt user
                        string formattedArguments = call.Arguments
                            .Replace("\\n", "\n")
                            .Replace("\\r", "\r")
                            .Replace("\\t", "\t")
                            .Replace("\\\"", "\"")
                            .Replace("\\'", "'")
                            .Replace("\\\\", "\\");
                        
                        string approvalMessage = "Run tool: " + call.Name + "\n\n" +
                                                "With arguments:\n" + formattedArguments + "?";
                        
                        DialogResult result = MessageBox.Show(
                            approvalMessage,
                            "Tool Call",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button2
                        );

                        if (result == DialogResult.Yes)
                        {
                            registry.ExecuteToolCall(call.Name, call.Arguments, out toolContent, out exitCode);
                        }
                        else
                        {
                            // User declined - return cancellation message
                            exitCode = -1;
                            toolContent = ToolRegistry.FormatCommandResult(
                                call.Name,
                                "Tool execution was cancelled by the user.",
                                exitCode
                            );
                        }
                    }
                    else
                    {
                        registry.ExecuteToolCall(call.Name, call.Arguments, out toolContent, out exitCode);
                    }

                    ChatMessage toolMsg = new ChatMessage
                    {
                        Role = "tool",
                        Content = toolContent,
                        ToolCallId = call.Id
                    };
                    conversation.Add(toolMsg);

                    if (!outputOnly)
                    {
                        Console.WriteLine("[tool output]");
                        if (showToolOutput)
                        {
                            Console.Write(toolContent);
                            if (!toolContent.EndsWith("\n"))
                            {
                                Console.WriteLine(); // Add newline if not present
                            }
                        }
                        else
                        {
                            // Show only the exit code
                            Console.WriteLine("Exit Code: " + exitCode);
                        }
                    }
                }

                // Run loop again so assistant can ingest tool output
                continue;
            }

            // Add assistant message
            ChatMessage assistantMsg = new ChatMessage
            {
                Role = "assistant",
                Content = response.Content
            };
            conversation.Add(assistantMsg);
            
            if (!outputOnly)
            {
                Console.WriteLine(); // Add newline after assistant response
            }
            break;
        }
    }



    private JObject BuildMessageObject(ChatMessage msg)
    {
        JObject msgObj = new JObject
        {
            ["role"] = msg.Role
        };

        if (!string.IsNullOrEmpty(msg.ToolCallId))
            msgObj["tool_call_id"] = msg.ToolCallId;

        if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
        {
            msgObj["content"] = msg.Content ?? "";
            JArray toolCallsArray = new JArray();

            foreach (var call in msg.ToolCalls)
            {
                JObject toolObj = new JObject
                {
                    ["id"] = call.Id ?? "",
                    ["type"] = "function"
                };

                JObject functionObj = new JObject
                {
                    ["name"] = call.Name ?? "",
                    ["arguments"] = call.Arguments ?? ""
                };

                toolObj["function"] = functionObj;
                toolCallsArray.Add(toolObj);
            }

            msgObj["tool_calls"] = toolCallsArray;
        }
        else if (msg.Image != null)
        {
            JArray contentArray = new JArray();

            if (!string.IsNullOrEmpty(msg.Content))
            {
                JObject textPart = new JObject
                {
                    ["type"] = "text",
                    ["text"] = msg.Content
                };
                contentArray.Add(textPart);
            }

            if (!string.IsNullOrEmpty(msg.Image))
            {
                JObject imgPart = new JObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JObject
                    {
                        ["url"] = "data:image/png;base64," + msg.Image
                    }
                };
                contentArray.Add(imgPart);
            }

            if (contentArray.Count == 0)
            {
                JObject emptyText = new JObject
                {
                    ["type"] = "text",
                    ["text"] = ""
                };
                contentArray.Add(emptyText);
            }

            msgObj["content"] = contentArray;
        }
        else
        {
            msgObj["content"] = msg.Content ?? "";
        }

        return msgObj;
    }

    LLMCompletionResponse sendMessages(List<ChatMessage> conversation, List<string> enabledTools, Action<string> onReasoningChunk, Action<int> onReasoningSummary)
    {
        // Build payload
        JObject payload = new JObject
        {
            ["model"] = config.GetConfigValue("model")
        };

        // Messages
        JArray messages = new JArray();

        // System message
        JObject systemMsg = new JObject
        {
            ["role"] = "system",
            ["content"] = ConfigHandler.DecodeStoredPrompt(config.GetConfigValue("sysprompt"))
        };
        messages.Add(systemMsg);

        // Process all user messages in the conversation list
        if (conversation != null)
        {
            foreach (var msg in conversation)
            {
                messages.Add(BuildMessageObject(msg));
            }
        }

        payload["messages"] = messages;

        // Add tools if any are enabled
        if (enabledTools != null && enabledTools.Count > 0 && registry != null)
        {
            JArray toolsArray = registry.BuildToolsArray(enabledTools);
            if (toolsArray.Count > 0)
                payload["tools"] = toolsArray;
        }

        payload["stream"] = true;

        return SendHttpRequest(payload, onReasoningChunk, onReasoningSummary);
    }

    private LLMCompletionResponse SendHttpRequest(JObject payload, Action<string> onReasoningChunk, Action<int> onReasoningSummary)
    {
        LLMCompletionResponse completionResponse = new LLMCompletionResponse
        {
            Content = string.Empty,
            ToolCalls = new List<ToolRegistry.ToolCall>(),
            FinishReason = string.Empty
        };

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
                completionResponse = SseStreamParser.Parse(reader, onReasoningChunk, onReasoningSummary);
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

            if ((config.GetConfigValue("llmserver") ?? "").StartsWith("https:", StringComparison.OrdinalIgnoreCase) && IsTlsFailure(ex))
                return CurlClient.SendRequest(config.GetConfigValue("llmserver"), config.GetConfigValue("apikey"), payload, onReasoningChunk, onReasoningSummary);

            return new LLMCompletionResponse(reason, null, "request_failed");
        }

        return completionResponse;
    }

    private static bool IsTlsFailure(Exception ex)
    {
        WebException webEx = ex as WebException;
        if (webEx != null)
            return webEx.Status == WebExceptionStatus.SecureChannelFailure
                || webEx.Status == WebExceptionStatus.TrustFailure
                || webEx.Status == WebExceptionStatus.ConnectFailure
                || (webEx.InnerException != null && webEx.InnerException.GetType().Name.Contains("Authentication"));

        return ex.GetType().Name.Contains("Authentication")
            || ex.GetType().Name.Contains("Security")
            || (ex.InnerException != null && ex.InnerException.GetType().Name.Contains("Authentication"));
    }
}
}