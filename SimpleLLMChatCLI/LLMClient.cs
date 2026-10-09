using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SimpleLLMChatCLI.RAG;
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
    private readonly ConfigHandler config;
    private readonly ToolRegistry registry;
    private readonly Func<string, string, double?, bool> requestToolApproval;

    // Cached sysprompt + tools schema length (NyoCoder-style base overhead).
    // Cleared (set null) on /reload so the next request recomputes it.
    public int? BaseOverheadChars;

    public string ReasoningEffort { get; set; }

    public LLMClient(ConfigHandler config, ToolRegistry registry, Func<string, string, double?, bool> requestToolApproval = null)
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

            LLMCompletionResponse response = StreamModelCall(
                conversation, enabledTools, approvalSet, toolCallDisplay,
                outputOnly, contextInjections, startBlock, onContentStart);

            if (response.FinishReason == "request_failed")
            {
                if (!outputOnly)
                    ChatOutput.StartBlock();
                ChatOutput.WriteLine("Request to LLM Failed (" + response.Content + ")");
                break;
            }

            if (response.ToolCalls != null && response.ToolCalls.Count > 0)
            {
                if (!RunToolCalls(conversation, response.ToolCalls, enabledSet, approvalSet, toolOutputDisplay, outputOnly, startBlock))
                    return;

                MaybeSummarizeInBackground(
                    conversation,
                    outputOnly,
                    "\n\n[Continue from this context. The user's original request is being processed.]");
                continue;
            }

            conversation.Add(new ChatMessage
            {
                Role = "assistant",
                Content = response.Content
            });
            PublishStatusTokens(GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead());

            if (!outputOnly)
                ChatOutput.EndLine();
            break;
        }
    }

    /// <summary>
    /// One model call. Streams [tool call] tags as arguments arrive. A call that
    /// still needs approval is not tagged here; the approval prompt shows it.
    /// </summary>
    private LLMCompletionResponse StreamModelCall(
        List<ChatMessage> conversation,
        List<string> enabledTools,
        HashSet<string> approvalSet,
        ChatBlockDisplayMode toolCallDisplay,
        bool outputOnly,
        List<string> contextInjections,
        Action startBlock,
        Action onContentStart)
    {
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

                    currentToolCallSuppressed = !IsAutoToolApproval() && approvalSet.Contains(toolCall.Name);

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

        LLMCompletionResponse response = sendMessages(
            conversation, enabledTools, ChatOutput.Write, toolCallStreamCallback,
            onContentStart, startBlock, outputOnly, contextInjections);

        if (toolCallUiOpen)
        {
            if (!toolCallArgsEndedWithNewline)
                ChatOutput.WriteLine();
            ChatOutput.WriteLine("[/tool call]");
        }

        return response;
    }

    /// <summary>
    /// Records the assistant tool call, then approves and runs each call.
    /// Returns false when output-only mode must stop because a call needs approval.
    /// </summary>
    private bool RunToolCalls(
        List<ChatMessage> conversation,
        List<ToolRegistry.ToolCall> toolCalls,
        HashSet<string> enabledSet,
        HashSet<string> approvalSet,
        ChatBlockDisplayMode toolOutputDisplay,
        bool outputOnly,
        Action startBlock)
    {
        conversation.Add(new ChatMessage
        {
            Role = "assistant",
            Content = string.Empty,
            ToolCalls = toolCalls
        });

        for (int i = 0; i < toolCalls.Count; i++)
        {
            ToolRegistry.ToolCall call = toolCalls[i];

            int exitCode = 0;
            string toolContent = string.Empty;
            string toolImage = null;
            string toolImageMime = null;
            bool runTool = false;
            bool askUser = false;
            double? jevConfidence = null;

            if (!enabledSet.Contains(call.Name))
            {
                exitCode = -1;
                toolContent = ToolRegistry.FormatCommandResult(
                    call.Name,
                    "error: tool '" + call.Name + "' is disabled by configuration.",
                    exitCode
                );
            }
            else if (IsAutoToolApproval())
            {
                JevGateDecision decision = JevDecisionClient.Decide(
                    config,
                    call.Name,
                    call.Arguments,
                    registry.GetToolDescription(call.Name),
                    RecentMessagesForDecision(conversation));
                if (decision.Choice == JevGateChoice.Allow)
                {
                    if (!outputOnly && config.GetConfigBool("showjevconfidence", true))
                    {
                        if (startBlock != null)
                            startBlock();
                        string approved = decision.Confidence.HasValue
                            ? "[" + call.Name + " auto allowed, confidence " + decision.Confidence.Value.ToString("0.00", CultureInfo.InvariantCulture) + "]"
                            : "[" + call.Name + " auto allowed]";
                        ChatOutput.WriteLine(approved);
                    }
                    runTool = true;
                }
                else
                {
                    askUser = true;
                    jevConfidence = ConfidenceForDisplay(decision.Confidence);
                }
            }
            else if (approvalSet.Contains(call.Name))
            {
                askUser = true;
            }
            else
            {
                runTool = true;
            }

            if (askUser && outputOnly)
            {
                ChatOutput.WriteLine("Error: Model called " + call.Name + " which requires approval");
                return false;
            }

            // The tool call block itself was already streamed above (if not suppressed
            // for approval); only pad a blank line before the approval prompt here.
            if (askUser)
            {
                if (!outputOnly && startBlock != null)
                    startBlock();
                if (requestToolApproval(call.Name, call.Arguments, jevConfidence))
                    runTool = true;
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

            if (runTool)
            {
                registry.ExecuteToolCall(call.Name, call.Arguments, out toolContent, out exitCode, out toolImage, out toolImageMime);
            }

            conversation.Add(new ChatMessage
            {
                Role = "tool",
                Content = toolContent,
                ToolCallId = call.Id,
                Image = toolImage,
                ImageMime = toolImageMime
            });

            if (!outputOnly)
            {
                startBlock();
                ChatOutput.WriteLine("[tool output]");
                if (toolOutputDisplay == ChatBlockDisplayMode.Hidden)
                {
                    ChatOutput.WriteLine("Exit Code: " + exitCode);
                }
                else
                {
                    ChatOutput.Write(toolContent ?? "");
                    if (string.IsNullOrEmpty(toolContent) || !toolContent.EndsWith("\n"))
                        ChatOutput.WriteLine();
                }
                ChatOutput.WriteLine("[/tool output]");
            }
        }

        return true;
    }

    /// <summary>
    /// If context usage is high, summarize on this thread and replace the conversation
    /// with a compact summary message. The summary text itself is not shown.
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
        {
            int contextWindow = config.GetConfigInt("contextWindowSize", 0);
            int dropped = DropOldestMessagesUntilFit(conversation, contextWindow);
            int chars = GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead();
            PublishStatusTokens(chars);
            if (!outputOnly)
            {
                if (dropped > 0)
                    ChatOutput.WriteLine("[Summarization failed - dropped older messages]");
                if (TokenEstimator.ShouldSummarize(chars, contextWindow))
                    ChatOutput.WriteLine("[Summarization failed - current turn still exceeds the context window]");
            }
            return;
        }

        conversation.Clear();
        conversation.Add(new ChatMessage(
            "user",
            "[Previous conversation summary]\n" + summary + continueHint));

        if (!outputOnly)
            ChatOutput.WriteLine("[Conversation summarized - continuing...]");

        PublishStatusTokens(GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead());
    }

    /// <summary>
    /// Drops messages older than the latest user turn until usage is under the
    /// summarize threshold. The latest user message and everything after it stay.
    /// </summary>
    private int DropOldestMessagesUntilFit(List<ChatMessage> conversation, int contextWindowSize)
    {
        int keepFrom = 0;
        for (int i = conversation.Count - 1; i >= 0; i--)
        {
            if (string.Equals(conversation[i].Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                keepFrom = i;
                break;
            }
        }

        int dropped = 0;
        while (keepFrom > 0 && TokenEstimator.ShouldSummarize(
            GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead(),
            contextWindowSize))
        {
            conversation.RemoveAt(0);
            keepFrom--;
            dropped++;
        }
        return dropped;
    }

    private bool IsAutoToolApproval()
    {
        return string.Equals(
            config.GetConfigValue("toolapprovalmode"),
            "auto",
            StringComparison.OrdinalIgnoreCase);
    }

    private double? ConfidenceForDisplay(double? confidence)
    {
        if (!confidence.HasValue || !config.GetConfigBool("showjevconfidence", true))
            return null;
        return confidence;
    }

    private static List<JevContextMessage> RecentMessagesForDecision(List<ChatMessage> conversation)
    {
        var selected = new List<JevContextMessage>();
        if (conversation == null || conversation.Count == 0)
            return selected;

        int start = conversation.Count - JevDecisionClient.RecentMessageCount;
        if (start < 0)
            start = 0;
        for (int i = start; i < conversation.Count; i++)
            selected.Add(ToJevContextMessage(conversation[i]));
        return selected;
    }

    private static JevContextMessage ToJevContextMessage(ChatMessage message)
    {
        var text = new StringBuilder();
        if (!string.IsNullOrEmpty(message.Content))
            text.Append(message.Content);
        if (message.ToolCalls != null)
        {
            for (int i = 0; i < message.ToolCalls.Count; i++)
            {
                if (text.Length > 0)
                    text.Append('\n');
                text.Append(message.ToolCalls[i].Name ?? string.Empty);
                text.Append(' ');
                text.Append(message.ToolCalls[i].Arguments ?? string.Empty);
            }
        }

        return new JevContextMessage
        {
            Role = message.Role ?? string.Empty,
            Text = text.ToString()
        };
    }

    public void PublishStatusTokens(int characterCount)
    {
        if (Program.StatusPipe == null)
            return;

        Program.StatusPipe.PublishStatus(TokenEstimator.ApproximateTokens(characterCount));
    }

    /// <summary>
    /// Sum of message content, image data URLs, and tool-call name/arguments (excludes base overhead).
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

            if (!string.IsNullOrEmpty(msg.Image))
            {
                string mime = string.IsNullOrEmpty(msg.ImageMime) ? ImageEncoder.DefaultMime : msg.ImageMime;
                count += ("data:" + mime + ";base64," + msg.Image).Length;
            }

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