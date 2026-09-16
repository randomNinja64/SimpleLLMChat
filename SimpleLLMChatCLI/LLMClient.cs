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
public partial class LLMClient
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

    public struct ChatMessage
    {
        public string Role;
        public string Content;
        public string Image;
        public string ImageMime;
        public List<ToolRegistry.ToolCall> ToolCalls;
        public string ToolCallId;

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
            ToolCallId = "";
            Image = null;
            ImageMime = null;
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
        string imageMime,
        string assistantName,
        List<string> enabledTools,
        List<string> toolsRequiringApproval,
        bool outputOnly)
    {
        ChatBlockDisplayMode toolCallDisplay = config.GetChatBlockDisplayMode("toolcalldisplay", ChatBlockDisplayMode.Collapsed);
        ChatBlockDisplayMode toolOutputDisplay = config.GetChatBlockDisplayMode("tooloutputdisplay", ChatBlockDisplayMode.Shown);

        HashSet<string> enabledSet = new HashSet<string>(
            enabledTools ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        HashSet<string> approvalSet = new HashSet<string>(
            toolsRequiringApproval ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

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
            Image = image,
            ImageMime = string.IsNullOrEmpty(imageMime) ? null : imageMime
        });

        List<string> contextInjections = registry != null
            ? registry.GetContextInjections(enabledTools)
            : new List<string>();

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
            // Hidden: name only (no args). outputOnly: no tool-call UI at all.
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

                        currentToolCallSuppressed = approvalSet.Contains(toolCall.Name);

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
                    else if (!string.IsNullOrEmpty(toolCall.Arguments)
                        && !currentToolCallSuppressed
                        && toolCallDisplay != ChatBlockDisplayMode.Hidden)
                    {
                        ChatOutput.Write(toolCall.Arguments);
                        toolCallArgsEndedWithNewline = toolCall.Arguments.EndsWith("\n");
                    }
                };
            }

            LLMCompletionResponse response = sendMessages(conversation, enabledTools, ChatOutput.Write, toolCallStreamCallback, onContentStart, startBlock, outputOnly, contextInjections);

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
                    bool needsApproval = approvalSet.Contains(call.Name);

                    if (needsApproval && outputOnly)
                    {
                        ChatOutput.WriteLine("Error: Model called " + call.Name + " which requires approval");
                        return;
                    }

                    // The tool call block itself was already streamed above (if not suppressed
                    // for approval); only pad a blank line before the approval prompt here.
                    if (!outputOnly && needsApproval)
                        startBlock();

                    int exitCode = 0;
                    string toolContent;

                    string toolImage = null;
                    string toolImageMime = null;
                    if (!enabledSet.Contains(call.Name))
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
                            registry.ExecuteToolCall(call.Name, call.Arguments, out toolContent, out exitCode, out toolImage, out toolImageMime);
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
                        registry.ExecuteToolCall(call.Name, call.Arguments, out toolContent, out exitCode, out toolImage, out toolImageMime);
                    }

                    ChatMessage toolMsg = new ChatMessage
                    {
                        Role = "tool",
                        Content = toolContent,
                        ToolCallId = call.Id,
                        Image = toolImage,
                        ImageMime = toolImageMime
                    };
                    conversation.Add(toolMsg);

                    if (!outputOnly)
                    {
                        startBlock();
                        if (toolOutputDisplay == ChatBlockDisplayMode.Hidden)
                        {
                            ChatOutput.WriteLine("[tool output]");
                            ChatOutput.WriteLine("Exit Code: " + exitCode);
                        }
                        else
                        {
                            ChatOutput.WriteLine("[tool output]");
                            ChatOutput.Write(toolContent ?? "");
                            if (string.IsNullOrEmpty(toolContent) || !toolContent.EndsWith("\n"))
                                ChatOutput.WriteLine();
                            ChatOutput.WriteLine("[/tool output]");
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
                ChatOutput.EndLine();
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

    private string SummarizeConversation(List<ChatMessage> conversation)
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
}
}