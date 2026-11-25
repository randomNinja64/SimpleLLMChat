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

        /// <summary>
        /// Auto-RAG context retrieved for this user message, kept apart from what was
        /// typed: merged in only when the request is built, so replays and chat recall
        /// see the user's words alone. Null when nothing was retrieved.
        /// </summary>
        public string Context;

        public string Image;
        public string ImageMime;
        public List<ToolRegistry.ToolCall> ToolCalls;
        public string ToolCallId;

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
            Context = null;
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

    /// <summary>State that lives for one turn.</summary>
    private sealed class Turn
    {
        public List<ChatMessage> Conversation;
        public TurnSettings Settings;
        public StreamSink Sink;
        public List<string> ContextInjections;
        public int ConsecutiveFailedToolCalls;
    }

    /// <summary>
    /// The workspace's chats for retrieval to recall from (set by the session before
    /// each turn); null recalls none.
    /// </summary>
    public ChatRecallScope ChatRecall { get; set; }

    public void ProcessConversation(
        List<ChatMessage> conversation,
        string userMessage,
        string image,
        string imageMime,
        TurnSettings settings)
    {
        StreamSink sink = StreamSink.For(settings);
        Turn turn = new Turn { Conversation = conversation, Settings = settings, Sink = sink };

        MaybeSummarizeInBackground(turn, NextMessageHint);

        RagHost.FlushPendingErrorOnce();
        RagHost.WaitIfIndexing();

        string retrieved = null;
        if (config.Get(AppSettings.RagEnabled) || (ChatRecall != null && AutoRagContext.IsChatRecallEnabled(config)))
        {
            bool everyTurn = string.Equals(
                config.Get(AppSettings.RagRetrieveMode),
                "everyturn",
                StringComparison.OrdinalIgnoreCase);
            bool isNewChat = conversation == null || conversation.Count == 0;

            if (everyTurn || isNewChat)
            {
                AutoRagResult rag = AutoRagContext.TryRetrieve(config, userMessage, ChatRecall);
                if (settings.ShowDocStatus && !string.IsNullOrEmpty(rag.UserStatusLine))
                    sink.Notice(rag.UserStatusLine);
                if (settings.ShowChatStatus && !string.IsNullOrEmpty(rag.ChatStatusLine))
                    sink.Notice(rag.ChatStatusLine);
                retrieved = rag.Context;
            }
        }

        conversation.Add(new ChatMessage
        {
            Role = "user",
            Content = userMessage ?? string.Empty,
            Context = retrieved,
            Image = image,
            ImageMime = string.IsNullOrEmpty(imageMime) ? null : imageMime
        });

        turn.ContextInjections = registry != null
            ? registry.GetContextInjections(settings.EnabledTools)
            : new List<string>();

        while (true)
        {
            PublishStatusTokens(GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead());

            LLMCompletionResponse response = SendMessages(
                conversation, settings.EnabledTools, sink, turn.ContextInjections);
            sink.EndResponse();

            if (Cancel.Requested)
            {
                // Keep the partial reply; half-streamed tool calls are dropped.
                if (!string.IsNullOrEmpty(response.Content))
                    conversation.Add(new ChatMessage("assistant", response.Content));
                break;
            }

            if (response.FinishReason == "request_failed")
            {
                sink.Error("Request to LLM Failed (" + response.Content + ")");
                break;
            }

            if (response.ToolCalls != null && response.ToolCalls.Count > 0)
            {
                if (!RunToolCalls(turn, response))
                    return;

                if (Cancel.Requested)
                    break;

                if (settings.MaxConsecutiveFailures > 0
                    && turn.ConsecutiveFailedToolCalls >= settings.MaxConsecutiveFailures)
                {
                    sink.Error("[Stopped: " + turn.ConsecutiveFailedToolCalls + " consecutive tool calls failed]");
                    break;
                }

                MaybeSummarizeInBackground(
                    turn, "\n\n[Continue from this context. The user's original request is being processed.]");
                continue;
            }

            conversation.Add(new ChatMessage
            {
                Role = "assistant",
                Content = response.Content
            });
            PublishStatusTokens(GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead());

            sink.EndLine();
            break;
        }

        if (Cancel.Requested)
            sink.Notice("[Stopped]");
    }

    /// <summary>
    /// Records the assistant tool call, then approves and runs each call.
    /// Returns false when output-only mode must stop because a call needs approval.
    /// </summary>
    private bool RunToolCalls(Turn turn, LLMCompletionResponse response)
    {
        List<ChatMessage> conversation = turn.Conversation;
        TurnSettings settings = turn.Settings;
        StreamSink sink = turn.Sink;

        conversation.Add(new ChatMessage
        {
            Role = "assistant",
            // Keep any text the model sent alongside the calls so it stays in history.
            Content = response.Content ?? string.Empty,
            ToolCalls = response.ToolCalls
        });

        foreach (ToolRegistry.ToolCall call in response.ToolCalls)
        {
            int exitCode = 0;
            string toolContent = string.Empty;
            string toolImage = null;
            string toolImageMime = null;
            bool runTool = false;
            bool askUser = false;
            double? jevConfidence = null;

            if (Cancel.Requested)
            {
                // Stopped: skip this call; its result is filled in below.
            }
            else if (!settings.EnabledSet.Contains(call.Name))
            {
                exitCode = -1;
                toolContent = ToolRegistry.FormatCommandResult(
                    call.Name,
                    "error: tool '" + call.Name + "' is disabled by configuration.",
                    exitCode
                );
            }
            else if (settings.AutoApprovalSet.Contains(call.Name))
            {
                JevGateDecision decision = JevDecisionClient.Decide(
                    config,
                    call.Name,
                    call.Arguments,
                    registry.GetToolDescription(call.Name),
                    RecentMessagesForDecision(conversation));
                if (decision.Choice == JevGateChoice.Allow)
                {
                    if (settings.ShowJevConfidence)
                    {
                        sink.Notice(decision.Confidence.HasValue
                            ? "[" + call.Name + " auto allowed, confidence " + decision.Confidence.Value.ToString("0.00", CultureInfo.InvariantCulture) + "]"
                            : "[" + call.Name + " auto allowed]");
                    }
                    runTool = true;
                }
                else
                {
                    askUser = true;
                    if (settings.ShowJevConfidence)
                        jevConfidence = decision.Confidence;
                }
            }
            else if (settings.ApprovalSet.Contains(call.Name))
            {
                askUser = true;
            }
            else
            {
                runTool = true;
            }

            if (askUser && settings.OutputOnly)
            {
                ChatOutput.WriteLine("Error: Model called " + call.Name + " which requires approval");
                return false;
            }

            // The tool call block itself was already streamed (if not suppressed
            // for approval); the prompt that follows shows the call.
            if (askUser && !Cancel.Requested)
            {
                sink.BeginApproval();
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

            if (runTool && !Cancel.Requested)
            {
                registry.ExecuteToolCall(call.Name, call.Arguments, out toolContent, out exitCode, out toolImage, out toolImageMime);
            }

            // Every recorded call needs a result, or the API rejects the next request.
            if (Cancel.Requested)
            {
                exitCode = -1;
                toolContent = ToolRegistry.FormatCommandResult(
                    call.Name, "Tool execution was cancelled by the user.", exitCode);
                toolImage = null;
                toolImageMime = null;
            }

            ConsecutiveToolFailures.Record(
                ref turn.ConsecutiveFailedToolCalls, settings.MaxConsecutiveFailures, runTool, exitCode);

            conversation.Add(new ChatMessage
            {
                Role = "tool",
                Content = toolContent,
                ToolCallId = call.Id,
                Image = toolImage,
                ImageMime = toolImageMime
            });

            sink.ToolOutput(toolContent, exitCode);
        }

        return true;
    }

    /// <summary>
    /// If context usage is high, first clears old tool output (see ClearOldToolOutputs);
    /// if that is not enough, summarizes on this thread and replaces the conversation
    /// with a compact summary message. The summary text itself is not shown.
    /// </summary>
    private void MaybeSummarizeInBackground(Turn turn, string continueHint)
    {
        List<ChatMessage> conversation = turn.Conversation;
        StreamSink sink = turn.Sink;
        int contextWindow = config.Get(AppSettings.ContextWindowSize);

        int statusChars = CurrentCharacterCount(conversation);
        PublishStatusTokens(statusChars);

        if (!TokenEstimator.ShouldSummarize(statusChars, contextWindow))
            return;

        // Clearing alone is silent: the token counter shows it.
        bool enough;
        ClearOldToolOutputs(conversation, statusChars, false, out enough);
        if (enough)
            return;

        sink.Notice("[Context usage high - summarizing conversation...]");

        string summary = SummarizeConversation(conversation);
        if (Cancel.Requested)
            return;
        if (string.IsNullOrEmpty(summary))
        {
            int dropped = DropOldestMessagesUntilFit(conversation, contextWindow);
            int chars = CurrentCharacterCount(conversation);
            if (dropped > 0)
                sink.Notice("[Summarization failed - dropped older messages]");

            // Last resort: this turn's own tool output, all but the newest result.
            if (TokenEstimator.ShouldSummarize(chars, contextWindow)
                && ClearCurrentTurnToolOutputs(conversation) > 0)
                chars = CurrentCharacterCount(conversation);

            PublishStatusTokens(chars);
            if (TokenEstimator.ShouldSummarize(chars, contextWindow))
                sink.Notice("[Summarization failed - current turn still exceeds the context window]");
            return;
        }

        ReplaceWithSummary(conversation, summary, continueHint);
        sink.Notice("[Conversation summarized - continuing...]");
    }

    /// <summary>
    /// The /compact command: the same rule as automatic summarization, at any usage.
    /// All old tool output is cleared; if usage is then at or under the clearing
    /// target, that is all, otherwise the conversation is summarized. A failed
    /// summary keeps the conversation, less any cleared output. Only a summary prints
    /// anything; otherwise the token counter shows the change. Returns true when the
    /// conversation changed.
    /// </summary>
    public bool CompactConversation(List<ChatMessage> conversation, TurnSettings settings)
    {
        StreamSink sink = StreamSink.For(settings);

        if (conversation == null || conversation.Count == 0)
        {
            ChatOutput.MarkSeparated();
            return false;
        }

        bool enough;
        int cleared = ClearOldToolOutputs(conversation, CurrentCharacterCount(conversation), true, out enough);
        if (enough)
        {
            ChatOutput.MarkSeparated();
            return cleared > 0;
        }

        sink.Notice("[Summarizing conversation...]");

        string summary = SummarizeConversation(conversation);
        if (Cancel.Requested || string.IsNullOrEmpty(summary))
        {
            PublishStatusTokens(CurrentCharacterCount(conversation));
            if (!Cancel.Requested)
                sink.Error("[Summarization failed]");
            return cleared > 0;
        }

        ReplaceWithSummary(conversation, summary, NextMessageHint);
        sink.Notice("[Conversation summarized]");
        return true;
    }

    private const string NextMessageHint = "\n\n[Continue from this context. The user's next message follows.]";

    private const string ToolOutputHeaderEnd = "\nOutput:\n";
    private const string ClearedToolOutput = "[Output cleared to save context]";

    private int CurrentCharacterCount(List<ChatMessage> conversation)
    {
        return GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead();
    }

    /// <summary>
    /// Clears tool results before the latest user message, oldest first. The tool
    /// calls stay, so the model still sees what it ran; results already cleared are
    /// skipped. Automatic compaction stops as soon as usage is under the target
    /// (TokenEstimator.IsUnderClearTarget); with <paramref name="clearAll"/> (/compact)
    /// every old result is cleared. Returns the number cleared; <paramref name="enough"/>
    /// says whether usage ended under the target.
    /// </summary>
    private int ClearOldToolOutputs(List<ChatMessage> conversation, int beforeChars, bool clearAll, out bool enough)
    {
        int contextWindow = config.Get(AppSettings.ContextWindowSize);
        int end = LatestUserIndex(conversation);
        int chars = beforeChars;
        int cleared = 0;

        for (int i = 0; i < end; i++)
        {
            if (!clearAll && TokenEstimator.IsUnderClearTarget(chars, contextWindow))
                break;
            int saved;
            if (!TryClearToolOutput(conversation, i, out saved))
                continue;
            chars -= saved;
            cleared++;
        }
        enough = TokenEstimator.IsUnderClearTarget(chars, contextWindow);

        if (cleared > 0)
            PublishStatusTokens(chars);
        return cleared;
    }

    /// <summary>
    /// Clears this turn's tool results (after the latest user message), all but the
    /// newest, which the model is acting on. Returns the number cleared.
    /// </summary>
    private static int ClearCurrentTurnToolOutputs(List<ChatMessage> conversation)
    {
        int newest = -1;
        for (int i = conversation.Count - 1; i >= 0 && newest < 0; i--)
        {
            if (IsRole(conversation[i], "tool"))
                newest = i;
        }

        int cleared = 0;
        int saved;
        for (int i = LatestUserIndex(conversation) + 1; i < newest; i++)
        {
            if (TryClearToolOutput(conversation, i, out saved))
                cleared++;
        }
        return cleared;
    }

    /// <summary>
    /// Replaces a tool result's output (and any image) with a short note, keeping the
    /// "Command: / Exit Code:" header. False when it is not a tool result or would
    /// not get smaller. ChatMessage is a struct, so the change is stored back.
    /// </summary>
    private static bool TryClearToolOutput(List<ChatMessage> conversation, int index, out int savedChars)
    {
        savedChars = 0;
        ChatMessage message = conversation[index];
        if (!IsRole(message, "tool"))
            return false;

        string content = message.Content ?? string.Empty;
        int header = content.IndexOf(ToolOutputHeaderEnd, StringComparison.Ordinal);
        string replacement = header >= 0
            ? content.Substring(0, header + ToolOutputHeaderEnd.Length) + ClearedToolOutput
            : ClearedToolOutput;

        int before = GetMessageCharacterCount(message);
        int after = replacement.Length;
        if (after >= before)
            return false;

        message.Content = replacement;
        message.Image = null;
        message.ImageMime = null;
        conversation[index] = message;
        savedChars = before - after;
        return true;
    }

    private static bool IsRole(ChatMessage message, string role)
    {
        return string.Equals(message.Role, role, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Index of the latest user message, or 0 when there is none.</summary>
    private static int LatestUserIndex(List<ChatMessage> conversation)
    {
        for (int i = conversation.Count - 1; i >= 0; i--)
        {
            if (IsRole(conversation[i], "user"))
                return i;
        }
        return 0;
    }

    private void ReplaceWithSummary(List<ChatMessage> conversation, string summary, string continueHint)
    {
        conversation.Clear();
        conversation.Add(new ChatMessage(
            "user",
            "[Previous conversation summary]\n" + summary + continueHint));

        PublishStatusTokens(GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead());
    }

    /// <summary>
    /// Drops messages older than the latest user turn until usage is under the
    /// summarize threshold. The latest user message and everything after it stay.
    /// </summary>
    private int DropOldestMessagesUntilFit(List<ChatMessage> conversation, int contextWindowSize)
    {
        int keepFrom = LatestUserIndex(conversation);

        // Keep dropping past any tool results at the front: a tool result whose
        // assistant tool call was dropped is rejected by strict servers.
        int dropped = 0;
        while (keepFrom > 0
            && (TokenEstimator.ShouldSummarize(
                    GetConversationCharacterCount(conversation) + GetBaseCharacterOverhead(),
                    contextWindowSize)
                || string.Equals(conversation[0].Role, "tool", StringComparison.OrdinalIgnoreCase)))
        {
            conversation.RemoveAt(0);
            keepFrom--;
            dropped++;
        }
        return dropped;
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
        if (Program.Pipe == null)
            return;

        Program.Pipe.PublishStatus(TokenEstimator.ApproximateTokens(characterCount));
    }

    /// <summary>
    /// Sum of message content, a flat estimate per image, and tool-call name/arguments (excludes base overhead).
    /// </summary>
    public static int GetConversationCharacterCount(List<ChatMessage> conversation)
    {
        int count = 0;
        if (conversation == null)
            return count;

        foreach (var msg in conversation)
            count += GetMessageCharacterCount(msg);

        return count;
    }

    private static int GetMessageCharacterCount(ChatMessage msg)
    {
        int count = 0;
        if (!string.IsNullOrEmpty(msg.Content))
            count += msg.Content.Length;
        if (!string.IsNullOrEmpty(msg.Context))
            count += msg.Context.Length;

        if (!string.IsNullOrEmpty(msg.Image))
            count += TokenEstimator.ImageCharEstimate;

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

        // No tools, silent sink: a background pass.
        LLMCompletionResponse response = SendMessages(summaryConversation, new List<string>(), StreamSink.Silent);
        if (response.FinishReason == "request_failed")
            return string.Empty;

        return response.Content ?? string.Empty;
    }

    /// <summary>
    /// Asks the model for a short title for a chat from its first exchange. The
    /// raw answer, or null when the request fails; see ChatTranscript.CleanModelTitle.
    /// </summary>
    public string GenerateTitle(string prompt, string reply)
    {
        const int excerpt = 1000;
        Func<string, string> cut = text =>
            text == null ? string.Empty : text.Length <= excerpt ? text : text.Substring(0, excerpt);

        List<ChatMessage> request = new List<ChatMessage>
        {
            new ChatMessage("user",
                "Write a short title (3 to 6 words) for a chat that begins with the exchange below. " +
                "Reply with the title only, no quotes or punctuation at the end.\n\n" +
                "User: " + cut(prompt) + "\n\nAssistant: " + cut(reply))
        };

        // No tools, silent sink, like the summary.
        LLMCompletionResponse response = SendMessages(request, new List<string>(), StreamSink.Silent);
        if (response.FinishReason == "request_failed")
            return null;
        return response.Content;
    }
}
}