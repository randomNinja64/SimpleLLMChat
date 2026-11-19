using System;

namespace SimpleLLMChatCLI
{
    /// <summary>
    /// Where a turn's output goes. This base class shows nothing; it is used as-is
    /// for background passes such as summarization.
    /// </summary>
    internal class StreamSink
    {
        public static readonly StreamSink Silent = new StreamSink();

        public static StreamSink For(TurnSettings settings)
        {
            if (settings.OutputOnly)
                return new ContentOnlySink();
            return new ConsoleSink(settings);
        }

        /// <summary>True when reasoning is shown as its own block.</summary>
        public virtual bool ShowsReasoning { get { return false; } }

        /// <summary>Something informational happened, e.g. the conversation was summarized.</summary>
        public virtual void Notice(string text) { }

        /// <summary>The turn failed. Shown in output-only mode too.</summary>
        public virtual void Error(string text) { }

        /// <summary>Runs before the user is asked to approve a tool call.</summary>
        public virtual void BeginApproval() { }

        /// <summary>Runs after the final response of the turn.</summary>
        public virtual void EndLine() { }

        /// <summary>Runs before the first content chunk of each response.</summary>
        public virtual void ContentStart() { }
        public virtual void Content(string text) { }

        public virtual void ReasoningStarted() { }
        public virtual void Reasoning(string text) { }
        public virtual void ReasoningEnded() { }

        public virtual void ToolCallStarted(string name) { }
        public virtual void ToolCallArgs(string chunk) { }
        public virtual void ToolOutput(string content, int exitCode) { }

        /// <summary>Closes whatever one model response left open.</summary>
        public virtual void EndResponse() { }
    }

    /// <summary>
    /// Output-only mode: the model's response text and nothing else.
    /// </summary>
    internal class ContentOnlySink : StreamSink
    {
        private bool _inThink;

        // Some models inline <think>...</think> in their content. The terminal
        // shows the tags as sent; pipe clients get a thinking block instead.
        public override void Content(string text)
        {
            while (true)
            {
                string tag = _inThink ? "</think>" : "<think>";
                int at = text.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                {
                    ChatOutput.Write(text);
                    return;
                }

                ChatOutput.Write(text.Substring(0, at));
                if (_inThink)
                    ChatOutput.EndBlock(text.Substring(at, tag.Length));
                else
                    ChatOutput.BeginBlock(ChatBlockKind.Thinking, null, text.Substring(at, tag.Length));
                _inThink = !_inThink;
                text = text.Substring(at + tag.Length);
            }
        }

        public override void Error(string text)
        {
            ChatOutput.WriteLine(text);
        }

        public override void EndResponse()
        {
            if (_inThink)
                ChatOutput.EndBlock(null);
            _inThink = false;
        }
    }

    /// <summary>
    /// Interactive mode: response text plus thinking, tool call and tool output blocks.
    /// The assistant name is printed once per turn:
    ///   "LLM: output"  when the response starts with plain output, or
    ///   "LLM:" on its own block when a [thinking]/[tool ...] block comes first.
    /// </summary>
    internal sealed class ConsoleSink : ContentOnlySink
    {
        private readonly TurnSettings _settings;
        private bool _headerPrinted;
        private bool _reasoningEndedWithNewline;
        private DateTime _reasoningStart;
        private bool _toolCallOpen;
        private bool _toolCallArgsEndedWithNewline = true;
        private bool _toolCallSuppressed;

        public ConsoleSink(TurnSettings settings)
        {
            _settings = settings;
        }

        public override bool ShowsReasoning
        {
            get { return _settings.ThinkingDisplay != ChatBlockDisplayMode.Hidden; }
        }

        // Every block is separated from the last by one blank line.
        // Thinking and tool blocks also print the assistant name if it is still due.
        private void StartBlock()
        {
            ChatOutput.StartBlock();
            if (!_headerPrinted)
            {
                _headerPrinted = true;
                ChatOutput.WriteLine(_settings.AssistantName + ":");
                ChatOutput.StartBlock();
            }
        }

        public override void Notice(string text)
        {
            ChatOutput.StartBlock();
            ChatOutput.WriteLine(text);
        }

        public override void Error(string text)
        {
            Notice(text);
        }

        public override void BeginApproval()
        {
            StartBlock();
        }

        public override void EndLine()
        {
            ChatOutput.EndLine();
        }

        public override void ContentStart()
        {
            ChatOutput.StartBlock();
            if (!_headerPrinted)
            {
                _headerPrinted = true;
                ChatOutput.Write(_settings.AssistantName + ": ");
            }
        }

        public override void ReasoningStarted()
        {
            if (ShowsReasoning)
            {
                StartBlock();
                ChatOutput.BeginBlock(ChatBlockKind.Thinking, null, "[thinking]\n");
            }
            else
            {
                _reasoningStart = DateTime.UtcNow;
            }
        }

        public override void Reasoning(string text)
        {
            if (!ShowsReasoning)
                return;

            // Straight to ChatOutput: reasoning text is not scanned for inline <think> tags.
            ChatOutput.Write(text);
            _reasoningEndedWithNewline = text.EndsWith("\n");
        }

        public override void ReasoningEnded()
        {
            if (ShowsReasoning)
            {
                // Closing tag on its own line; the next block's StartBlock adds the blank line.
                if (!_reasoningEndedWithNewline)
                    ChatOutput.Write("\n");
                ChatOutput.EndBlock("[/thinking]\n");
            }
            else
            {
                int secs = Math.Max(1, (int)(DateTime.UtcNow - _reasoningStart).TotalSeconds);
                StartBlock();
                Content("[thought for " + secs + " second" + (secs == 1 ? "" : "s") + "]\n");
            }
        }

        // A call that still needs approval is not tagged here; the approval prompt shows it.
        public override void ToolCallStarted(string name)
        {
            CloseToolCall();
            _toolCallSuppressed = !_settings.AutoApproval && _settings.ApprovalSet.Contains(name);
            if (_toolCallSuppressed)
                return;

            StartBlock();
            ChatOutput.BeginBlock(ChatBlockKind.ToolCall, name, "[tool call] " + name + "\n");
            _toolCallOpen = true;
            _toolCallArgsEndedWithNewline = true;
        }

        public override void ToolCallArgs(string chunk)
        {
            if (_toolCallSuppressed || _settings.ToolCallDisplay == ChatBlockDisplayMode.Hidden)
                return;

            ChatOutput.Write(chunk);
            _toolCallArgsEndedWithNewline = chunk.EndsWith("\n");
        }

        public override void ToolOutput(string content, int exitCode)
        {
            StartBlock();
            ChatOutput.BeginBlock(ChatBlockKind.ToolOutput, null, "[tool output]\n");
            if (_settings.ToolOutputDisplay == ChatBlockDisplayMode.Hidden)
            {
                ChatOutput.WriteLine("Exit Code: " + exitCode);
            }
            else
            {
                ChatOutput.Write(content ?? "");
                if (string.IsNullOrEmpty(content) || !content.EndsWith("\n"))
                    ChatOutput.WriteLine();
            }
            ChatOutput.EndBlock("[/tool output]\n");
        }

        public override void EndResponse()
        {
            base.EndResponse();
            CloseToolCall();
            _toolCallSuppressed = false;
        }

        private void CloseToolCall()
        {
            if (!_toolCallOpen)
                return;

            if (!_toolCallArgsEndedWithNewline)
                ChatOutput.WriteLine();
            ChatOutput.EndBlock("[/tool call]\n");
            _toolCallOpen = false;
        }
    }
}
