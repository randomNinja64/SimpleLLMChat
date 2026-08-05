using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SimpleLLMChatCLI.RAG;
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
    // Cleared (set null) on /reload so the next request recomputes it.
    public int? BaseOverheadChars;

    public string ReasoningEffort { get; set; }

    public LLMClient(ConfigHandler config, ToolRegistry registry, Func<string, string, bool> requestToolApproval = null)
    {
        this.registry = registry;
        this.config = config;
        this.requestToolApproval = requestToolApproval ?? CliRequestApproval;
    }

    // Struct for chat messages
    public struct ChatMessage
    {
        public string Role;
        public string Content;
        public string Image;
        public List<ToolRegistry.ToolCall> ToolCalls;
        public string ToolCallId;

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
            ToolCallId = "";
            Image = null;
            ToolCalls = new List<ToolRegistry.ToolCall>();
        }
    }

    public struct LLMCompletionResponse
    {
        public string Content;
        public List<ToolRegistry.ToolCall> ToolCalls;
        public string FinishReason;

        public LLMCompletionResponse(string content, List<ToolRegistry.ToolCall> toolCalls, string finishReason)
        {
            Content = content;
            ToolCalls = toolCalls ?? new List<ToolRegistry.ToolCall>();
            FinishReason = finishReason;
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

        RagHost.FlushPendingErrorOnce();
        RagHost.WaitIfIndexing();

        string contentForLlm = userMessage ?? string.Empty;
        if (config.GetConfigBool("ragEnabled", false))
        {
            bool everyTurn = string.Equals(
                config.GetConfigValue("ragRetrieveMode", "newchat"),
                "everyturn",
                StringComparison.OrdinalIgnoreCase);
            bool isNewChat = conversation == null || conversation.Count == 0;
            
            // If RAG is enabled and the retrieve mode is set to every turn or it's a new chat, retrieve the context.
            if (everyTurn || isNewChat)
            {
                AutoRagResult rag = AutoRagContext.TryRetrieve(config, userMessage, contentForLlm);
                if (!outputOnly && !string.IsNullOrEmpty(rag.UserStatusLine))
                    ChatOutput.WriteLine(rag.UserStatusLine);
                contentForLlm = rag.MergedPrompt;
            }
        }

        conversation.Add(new ChatMessage
        {
            Role = "user",
            Content = contentForLlm,
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

            // Stream tool calls with explicit open/close markers as they arrive, rather than
            // waiting for the full response before printing them. A tool that requires approval
            // is suppressed here since the approval prompt below shows its name + args instead.
            bool toolCallUiOpen = false;
            bool toolCallArgsEndedWithNewline = true;
            bool currentToolCallSuppressed = false;
            Action<ToolRegistry.ToolCall> toolCallStreamCallback = null;
            if (!outputOnly)
            {
                toolCallStreamCallback = (toolCall) =>
                {
                    if (!string.IsNullOrEmpty(toolCall.Name) && string.IsNullOrEmpty(toolCall.Arguments))
                    {
                        if (toolCallUiOpen)
                        {
                            if (!toolCallArgsEndedWithNewline)
                                ChatOutput.WriteLine();
                            ChatOutput.WriteLine("[/tool call]");
                        }

                        currentToolCallSuppressed = toolsRequiringApproval != null
                            && toolsRequiringApproval.Contains(toolCall.Name);

                        if (!currentToolCallSuppressed)
                        {
                            startBlock();
                            ChatOutput.WriteLine("[tool call] " + toolCall.Name);
                            toolCallUiOpen = true;
                            toolCallArgsEndedWithNewline = true;
                        }
                        else
                        {
                            toolCallUiOpen = false;
                        }
                    }
                    else if (!string.IsNullOrEmpty(toolCall.Arguments) && !currentToolCallSuppressed)
                    {
                        ChatOutput.Write(toolCall.Arguments);
                        toolCallArgsEndedWithNewline = toolCall.Arguments.EndsWith("\n");
                    }
                };
            }

            LLMCompletionResponse response = sendMessages(conversation, enabledTools, ChatOutput.Write, toolCallStreamCallback, onContentStart, startBlock);

            if (toolCallUiOpen)
            {
                if (!toolCallArgsEndedWithNewline)
                    ChatOutput.WriteLine();
                ChatOutput.WriteLine("[/tool call]");
            }

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
                    bool needsApproval = toolsRequiringApproval != null
                        && toolsRequiringApproval.Contains(call.Name);

                    // The tool call block itself was already streamed above (if not suppressed
                    // for approval); only pad a blank line before the approval prompt here.
                    if (!outputOnly && needsApproval)
                        startBlock();

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
                    else if (needsApproval)
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
    /// Cached system prompt + tool schema length. 0 until the first request is built
    /// or <see cref="RefreshBaseCharacterOverhead"/> runs (e.g. after /reload).
    /// </summary>
    public int GetBaseCharacterOverhead()
    {
        return BaseOverheadChars.HasValue ? BaseOverheadChars.Value : 0;
    }

    /// <summary>
    /// Recomputes system prompt + tool schema overhead from the current config/tools.
    /// Used after /reload so status stays accurate without waiting for the next request.
    /// </summary>
    public void RefreshBaseCharacterOverhead(List<string> enabledTools)
    {
        string systemPrompt = BuildSystemPrompt(enabledTools);
        int toolsChars = 0;
        if (enabledTools != null && enabledTools.Count > 0 && registry != null)
        {
            JArray toolsArray = registry.BuildToolsArray(enabledTools);
            if (toolsArray.Count > 0)
                toolsChars = toolsArray.ToString(Formatting.None).Length;
        }
        BaseOverheadChars = systemPrompt.Length + toolsChars;
    }

    private string BuildSystemPrompt(List<string> enabledTools)
    {
        string sysprompt = ConfigHandler.DecodeStoredPrompt(config.GetConfigValue("sysprompt")) ?? "";

        if (registry != null)
        {
            foreach (string injection in registry.GetContextInjections(enabledTools))
                sysprompt += "\n\n" + injection;
        }

        string ragHint = RagHost.GetKnowledgePathHint(config);
        if (!string.IsNullOrEmpty(ragHint))
        {
            if (sysprompt.Length > 0)
                sysprompt += "\n\n";
            sysprompt += ragHint;
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
        if (!BaseOverheadChars.HasValue)
        {
            List<string> configuredTools = config.GetConfigList("tools");
            bool summarizationPass = (enabledTools == null || enabledTools.Count == 0)
                && configuredTools != null && configuredTools.Count > 0;
            if (!summarizationPass)
                BaseOverheadChars = systemPrompt.Length + toolsChars;
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
        Action<ToolRegistry.ToolCall> toolCallCallback = null,
        Action onContentStart = null,
        Action startBlock = null)
    {
        return SendHttpRequest(BuildRequestPayload(conversation, enabledTools), outputCallback, toolCallCallback, onContentStart, startBlock);
    }

    private LLMCompletionResponse SendHttpRequest(JObject payload, Action<string> outputCallback = null,
        Action<ToolRegistry.ToolCall> toolCallCallback = null,
        Action onContentStart = null, Action startBlock = null)
    {
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

            // Try curl fallback for HTTPS connection errors
            string serverUrl = config.GetConfigValue("llmserver") ?? "";
            if (CurlClient.CanFallback(serverUrl, ex))
                return CurlClient.SendRequest(serverUrl, config.GetConfigValue("apikey"), payload, outputCallback, onReasoningChunk, onReasoningSummary, toolCallCallback, onContentStart, startBlock);

            return new LLMCompletionResponse(reason, null, "request_failed");
        }
    }

    /// <summary>
    /// Console approval prompt routed through ChatOutput so block spacing stays
    /// accurate. Wire format is FormatApprovalMessage + ApprovalPrompt (the GUI
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
}
}