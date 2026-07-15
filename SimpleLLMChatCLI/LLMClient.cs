using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace SimpleLLMChatCLI
{
public class LLMClient
{
    private readonly ConfigHandler config;
    private readonly ToolRegistry registry;
    private readonly Func<string, string, bool> requestToolApproval;

    // Cached sysprompt + tools schema length (NyoCoder-style base overhead).
    private int? _baseOverheadChars;

    public string ReasoningEffort { get; set; }

    public LLMClient(ConfigHandler config, ToolRegistry registry, Func<string, string, bool> requestToolApproval = null)
    {
        this.registry = registry;
        this.config = config;
        this.requestToolApproval = requestToolApproval ?? CliRequestApproval;
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
        // If prior context is already high, summarize it silently first, then run the user prompt.
        MaybeSummarizeInBackground(
            conversation,
            outputOnly,
            "\n\n[Continue from this context. The user's next message follows.]");

        conversation.Add(new ChatMessage
        {
            Role = "user",
            Content = userMessage,
            Image = image
        });

        // The assistant name is printed once per turn:
        //   "LLM: output"  when the response starts with plain output, or
        //   "LLM:" on its own block when a [thinking]/[tool ...] block comes first.
        bool assistantHeaderPrinted = false;
        Action startBlock = null;
        Action onContentStart = null;
        if (!outputOnly)
        {
            // Runs before tagged blocks ([thinking], [thought for ...], [tool ...], errors).
            startBlock = () =>
            {
                ChatOutput.StartBlock();
                if (!assistantHeaderPrinted)
                {
                    assistantHeaderPrinted = true;
                    ChatOutput.WriteLine(assistantName + ":");
                    ChatOutput.StartBlock();
                }
            };
            // Runs before the first content chunk of each response.
            onContentStart = () =>
            {
                ChatOutput.StartBlock();
                if (!assistantHeaderPrinted)
                {
                    assistantHeaderPrinted = true;
                    ChatOutput.Write(assistantName + ": ");
                }
            };
        }

        while (true)
        {
            PublishStatusTokens(GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead());

            LLMCompletionResponse response = sendMessages(conversation, enabledTools, ChatOutput.Write, onContentStart, startBlock);

            if (response.FinishReason == "request_failed")
            {
                if (!outputOnly)
                    ChatOutput.StartBlock();
                ChatOutput.WriteLine("Request to LLM Failed (" + response.Content + ")");
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
                        startBlock();
                        ChatOutput.WriteLine("[tool request] " + call.Name + " with arguments: " + call.Arguments);
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
                        if (requestToolApproval(call.Name, call.Arguments))
                        {
                            registry.ExecuteToolCall(call.Name, call.Arguments, out toolContent, out exitCode);
                        }
                        else
                        {
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
                        startBlock();
                        ChatOutput.WriteLine("[tool output]");
                        if (showToolOutput)
                        {
                            ChatOutput.Write(toolContent);
                            if (!toolContent.EndsWith("\n"))
                            {
                                ChatOutput.WriteLine(); // Add newline if not present
                            }
                        }
                        else
                        {
                            // Show only the exit code
                            ChatOutput.WriteLine("Exit Code: " + exitCode);
                        }
                    }
                }

                // Mid-turn (after tools): compact context silently, then continue the user request.
                MaybeSummarizeInBackground(
                    conversation,
                    outputOnly,
                    "\n\n[Continue from this context. The user's original request is being processed.]");
                continue;
            }

            // Add assistant message
            ChatMessage assistantMsg = new ChatMessage
            {
                Role = "assistant",
                Content = response.Content
            };
            conversation.Add(assistantMsg);
            PublishStatusTokens(GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead());

            if (!outputOnly)
            {
                ChatOutput.EndLine(); // Terminate the response line if the model didn't
            }
            break;
        }
    }

    /// <summary>
    /// If context usage is high, summarize silently (summary text is not shown) and replace
    /// the conversation with a compact summary message.
    /// </summary>
    private void MaybeSummarizeInBackground(List<ChatMessage> conversation, bool outputOnly, string continueHint)
    {
        int statusChars = GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead();
        PublishStatusTokens(statusChars);

        if (!TokenEstimator.ShouldSummarize(statusChars, config.GetConfigInt("contextWindowSize", 0)))
            return;

        if (!outputOnly)
        {
            ChatOutput.StartBlock();
            ChatOutput.WriteLine("[Context usage high - summarizing conversation...]");
        }

        string summary = SummarizeConversation(conversation);
        if (string.IsNullOrEmpty(summary))
            return;

        conversation.Clear();
        conversation.Add(new ChatMessage(
            "user",
            "[Previous conversation summary]\n" + summary + continueHint));

        if (!outputOnly)
            ChatOutput.WriteLine("[Conversation summarized - continuing...]");

        PublishStatusTokens(GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead());
    }

    public void PublishStatusTokens(int characterCount)
    {
        if (Program.StatusPipe == null)
            return;

        Program.StatusPipe.PublishStatus(TokenEstimator.ApproximateTokens(characterCount));
    }

    /// <summary>
    /// Sum of message content and tool-call name/arguments (excludes base overhead).
    /// </summary>
    public static int GetConversationCharacterCount(List<ChatMessage> conversation)
    {
        int count = 0;
        if (conversation == null)
            return count;

        foreach (var msg in conversation)
        {
            if (!string.IsNullOrEmpty(msg.Content))
                count += msg.Content.Length;

            if (msg.ToolCalls != null)
            {
                foreach (var toolCall in msg.ToolCalls)
                {
                    if (!string.IsNullOrEmpty(toolCall.Name))
                        count += toolCall.Name.Length;
                    if (!string.IsNullOrEmpty(toolCall.Arguments))
                        count += toolCall.Arguments.Length;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Cached system prompt + tool schema length. 0 until the first request is built.
    /// </summary>
    public int GetBaseCharacterOverhead()
    {
        return _baseOverheadChars.HasValue ? _baseOverheadChars.Value : 0;
    }

    private string BuildSystemPrompt(List<string> enabledTools)
    {
        string sysprompt = ConfigHandler.DecodeStoredPrompt(config.GetConfigValue("sysprompt")) ?? "";

        if (registry != null)
        {
            foreach (string injection in registry.GetContextInjections(enabledTools))
                sysprompt += "\n\n" + injection;
        }

        return sysprompt;
    }

    private JObject BuildRequestPayload(List<ChatMessage> conversation, List<string> enabledTools)
    {
        JObject payload = new JObject
        {
            ["model"] = config.GetConfigValue("model")
        };

        string systemPrompt = BuildSystemPrompt(enabledTools);

        JArray messages = new JArray();
        messages.Add(new JObject
        {
            ["role"] = "system",
            ["content"] = systemPrompt
        });

        if (conversation != null)
        {
            foreach (var msg in conversation)
                messages.Add(BuildMessageObject(msg));
        }

        payload["messages"] = messages;

        int toolsChars = 0;
        if (enabledTools != null && enabledTools.Count > 0 && registry != null)
        {
            JArray toolsArray = registry.BuildToolsArray(enabledTools);
            if (toolsArray.Count > 0)
            {
                payload["tools"] = toolsArray;
                toolsChars = toolsArray.ToString(Formatting.None).Length;
            }
        }

        // Capture overhead from a normal request only (not summarization with tools disabled).
        if (!_baseOverheadChars.HasValue)
        {
            List<string> configuredTools = config.GetConfigList("tools");
            bool summarizationPass = (enabledTools == null || enabledTools.Count == 0)
                && configuredTools != null && configuredTools.Count > 0;
            if (!summarizationPass)
                _baseOverheadChars = systemPrompt.Length + toolsChars;
        }

        payload["stream"] = true;

        if (!string.IsNullOrEmpty(ReasoningEffort))
            payload["reasoning_effort"] = ReasoningEffort;

        return payload;
    }

    /// <summary>
    /// Asks the model for a concise summary of the conversation (tools disabled).
    /// Runs silently — summary text is not streamed to the user.
    /// </summary>
    public string SummarizeConversation(List<ChatMessage> conversation)
    {
        if (conversation == null || conversation.Count == 0)
            return string.Empty;

        List<ChatMessage> summaryConversation = new List<ChatMessage>(conversation);
        summaryConversation.Add(new ChatMessage(
            "user",
            "Please provide a concise summary of our conversation so far. " +
            "Focus on: the main topics discussed, important details or decisions, " +
            "and anything that still needs follow-up."));

        // No tools, null output — silent background pass.
        LLMCompletionResponse response = sendMessages(summaryConversation, new List<string>(), null);
        if (response.FinishReason == "request_failed")
            return string.Empty;

        return response.Content ?? string.Empty;
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

    LLMCompletionResponse sendMessages(
        List<ChatMessage> conversation,
        List<string> enabledTools,
        Action<string> outputCallback = null,
        Action onContentStart = null,
        Action startBlock = null)
    {
        return SendHttpRequest(BuildRequestPayload(conversation, enabledTools), outputCallback, onContentStart, startBlock);
    }

    private LLMCompletionResponse SendHttpRequest(JObject payload, Action<string> outputCallback = null,
        Action onContentStart = null, Action startBlock = null)
    {
        LLMCompletionResponse completionResponse = new LLMCompletionResponse
        {
            Content = string.Empty,
            ToolCalls = new List<ToolRegistry.ToolCall>(),
            FinishReason = string.Empty
        };

        // NyoCoder-style: wire reasoning from config when streaming output is enabled.
        bool showReasoning = outputCallback != null && config.GetConfigBool("showreasoningoutput");
        Action<string> onReasoningChunk = showReasoning ? outputCallback : null;
        Action<int> onReasoningSummary = null;
        if (outputCallback != null && !showReasoning)
        {
            onReasoningSummary = s =>
            {
                startBlock?.Invoke();
                outputCallback("[thought for " + s + " second" + (s == 1 ? "" : "s") + "]\n");
            };
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
                completionResponse = SseStreamParser.Parse(reader, outputCallback, onReasoningChunk, onReasoningSummary, onContentStart, startBlock);
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

            // Try curl fallback for HTTPS connection errors
            string serverUrl = config.GetConfigValue("llmserver") ?? "";
            string curlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "curl.exe");
            if (serverUrl.StartsWith("https:", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(curlPath) && ShouldFallbackToCurl(ex))
                return CurlClient.SendRequest(serverUrl, config.GetConfigValue("apikey"), payload, outputCallback, onReasoningChunk, onReasoningSummary, onContentStart, startBlock);

            return new LLMCompletionResponse(reason, null, "request_failed");
        }

        return completionResponse;
    }

    /// <summary>
    /// Console approval prompt routed through ChatOutput so block spacing stays
    /// accurate. Same wire format as ToolApproval.RequestApproval (the GUI
    /// parses the "Run tool:" ... "Approve? (Y/N): " block from stdout).
    /// </summary>
    private static bool CliRequestApproval(string toolName, string arguments)
    {
        ChatOutput.WriteLine(ToolApproval.FormatApprovalMessage(toolName, arguments));
        Console.Out.Flush();

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

    private static bool ShouldFallbackToCurl(Exception ex)
    {
        WebException webEx = ex as WebException;
        if (webEx != null)
            return webEx.Status == WebExceptionStatus.SecureChannelFailure
                || webEx.Status == WebExceptionStatus.TrustFailure
                || webEx.Status == WebExceptionStatus.ConnectFailure
                || webEx.Status == WebExceptionStatus.ConnectionClosed
                || webEx.Status == WebExceptionStatus.SendFailure
                || webEx.Status == WebExceptionStatus.ReceiveFailure
                || webEx.Status == WebExceptionStatus.Timeout
                || webEx.Status == WebExceptionStatus.ServerProtocolViolation
                || (webEx.InnerException != null && webEx.InnerException.GetType().Name.Contains("Authentication"));

        return ex.GetType().Name.Contains("Authentication")
            || ex.GetType().Name.Contains("Security")
            || ex.GetType().Name.Contains("IOException")
            || (ex.Message != null && ex.Message.Contains("connection"));
    }
}
}