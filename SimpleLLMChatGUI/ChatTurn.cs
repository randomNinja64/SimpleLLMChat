using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// One chat turn (user or assistant) backed by its own FlowDocument.
    /// </summary>
    public class ChatTurn
    {
        /// <summary>
        /// Classic +/- expander style provided by <c>MainWindow</c>.
        /// </summary>
        public static Style ThinkingExpanderStyle { get; set; }

        private static readonly string[] CollapsibleOpenTags =
        {
            "[thinking]", "<think>", "[tool call]", "[tool output]"
        };
        private static readonly string[] CollapsibleCloseTags =
        {
            "[/thinking]", "</think>", "[/tool call]", "[/tool output]"
        };

        internal enum CollapsibleBlockKind
        {
            Thinking,
            ToolCall,
            ToolOutput
        }

        /// <summary>
        /// Shared UI state for thinking / tool-call expanders.
        /// </summary>
        internal sealed class CollapsibleBlockState
        {
            public bool Active;
            public string Name;
            public CollapsibleBlockKind Kind;
            public Expander Expander;
            public TextBlock HeaderLabel;
            public TextBlock BodyText;
            public DispatcherTimer Timer;
            public int EllipsisCount = 1;
            public DateTime StartedUtc;
            public int DurationSeconds;

            public bool Collapsed
            {
                get { return !Expander.IsExpanded; }
            }
        }

        public FlowDocument Document { get; private set; }

        /// <summary>
        /// Block index already processed by MarkdownHandler for this turn's document.
        /// </summary>
        public int MarkdownProcessedBlockCount;

        // False until visible text is appended; used to drop the CLI's
        // inter-turn padding newlines, which would otherwise render as
        // blank first lines in this turn's document.
        private bool _hasContent;

        private CollapsibleBlockState _activeBlock;

        public ChatTurn()
        {
            Document = new FlowDocument
            {
                PagePadding = new Thickness(0)
            };
        }

        /// <summary>
        /// Appends streamed text to this turn. Returns null when the whole
        /// string was consumed, or the leftover text when a collapsible block
        /// boundary was reached and the caller should continue in a new turn.
        /// </summary>
        public string AppendText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            if (!_hasContent)
            {
                // The CLI's own inter-turn padding newlines land unpredictably
                // once each turn is its own document — drop them. Turn-to-turn
                // spacing comes from ListBoxItem Margin.
                text = text.TrimStart('\r', '\n');
                if (text.Length == 0)
                    return null;
                _hasContent = true;

                // Always start real content in a fresh paragraph.
                Document.Blocks.Add(new Paragraph());
            }

            string remaining = text;
            while (!string.IsNullOrEmpty(remaining))
            {
                if (_activeBlock == null)
                {
                    int openIndex;
                    int openLength;
                    if (!TryFindTag(remaining, CollapsibleOpenTags, out openIndex, out openLength))
                    {
                        AppendPlain(remaining);
                        return null;
                    }

                    string openTag = remaining.Substring(openIndex, openLength);
                    if (openIndex > 0)
                        AppendPlain(remaining.Substring(0, openIndex));

                    // A collapsible block always opens a turn of its own, so
                    // batched tool calls are spaced by the chat list's item margin.
                    if (HasRenderedContent())
                        return remaining.Substring(openIndex);

                    remaining = remaining.Substring(openIndex + openLength);
                    if (openTag.Equals("[tool call]", StringComparison.OrdinalIgnoreCase))
                    {
                        string name;
                        remaining = TakeToolCallName(remaining, out name);
                        StartCollapsibleBlock(name, CollapsibleBlockKind.ToolCall);
                    }
                    else if (openTag.Equals("[tool output]", StringComparison.OrdinalIgnoreCase))
                    {
                        if (App.Config.GetChatBlockDisplayMode("tooloutputdisplay", ChatBlockDisplayMode.Shown) == ChatBlockDisplayMode.Hidden)
                        {
                            AppendPlain(openTag);
                            continue;
                        }

                        remaining = remaining.TrimStart('\r', '\n');
                        StartCollapsibleBlock(null, CollapsibleBlockKind.ToolOutput);
                    }
                    else
                    {
                        remaining = remaining.TrimStart('\r', '\n');
                        StartCollapsibleBlock(null, CollapsibleBlockKind.Thinking);
                    }
                }
                else
                {
                    int closeIndex;
                    int closeLength;
                    if (!TryFindTag(remaining, CollapsibleCloseTags, out closeIndex, out closeLength))
                    {
                        _activeBlock.BodyText.Text += remaining;
                        return null;
                    }

                    if (closeIndex > 0)
                        _activeBlock.BodyText.Text += remaining.Substring(0, closeIndex);

                    EndCollapsibleBlock();
                    return remaining.Substring(closeIndex + closeLength).TrimStart('\r', '\n');
                }
            }

            return null;
        }

        /// <summary>
        /// True once this turn holds something visible — an expander or a
        /// paragraph with text.
        /// </summary>
        public bool HasRenderedContent()
        {
            foreach (Block block in Document.Blocks)
            {
                if (block is BlockUIContainer)
                    return true;

                Paragraph paragraph = block as Paragraph;
                if (paragraph != null
                    && new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim().Length != 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Removes empty paragraphs left at the end of the document by the
        /// CLI's padding newlines before the next prompt.
        /// </summary>
        public void TrimTrailingBlankParagraphs()
        {
            if (_activeBlock != null)
                EndCollapsibleBlock();

            while (Document.Blocks.Count > 1)
            {
                Paragraph paragraph = Document.Blocks.LastBlock as Paragraph;
                if (paragraph == null)
                    break;

                string text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
                if (text.Trim().Length != 0)
                    break;

                Document.Blocks.Remove(paragraph);
            }
        }

        /// <summary>
        /// Applies font size to the document, markdown headers, and collapsible expanders.
        /// </summary>
        public void ApplyFontSize(double fontSize)
        {
            Document.FontSize = fontSize;
            MarkdownHandler.ApplyHeaderFontSizes(Document, fontSize);
            foreach (Block block in Document.Blocks)
            {
                BlockUIContainer container = block as BlockUIContainer;
                if (container == null)
                    continue;

                Expander expander = container.Child as Expander;
                if (expander == null)
                    continue;

                TextBlock body = expander.Content as TextBlock;
                if (body != null)
                    body.FontSize = fontSize;

                TextBlock header = expander.Header as TextBlock;
                if (header != null)
                    header.FontSize = fontSize;
            }
        }

        private void AppendPlain(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            new TextRange(Document.ContentEnd, Document.ContentEnd).Text = text;
        }

        private void StartCollapsibleBlock(string name, CollapsibleBlockKind kind)
        {
            // Drop an empty trailing paragraph left by content setup so the
            // expander is the next visible block (no blank line above it).
            Paragraph last = Document.Blocks.LastBlock as Paragraph;
            if (last != null
                && string.IsNullOrWhiteSpace(new TextRange(last.ContentStart, last.ContentEnd).Text))
            {
                Document.Blocks.Remove(last);
            }

            ChatBlockDisplayMode mode = kind == CollapsibleBlockKind.ToolCall
                ? App.Config.GetChatBlockDisplayMode("toolcalldisplay", ChatBlockDisplayMode.Collapsed)
                : kind == CollapsibleBlockKind.ToolOutput
                    ? App.Config.GetChatBlockDisplayMode("tooloutputdisplay", ChatBlockDisplayMode.Shown)
                    : App.Config.GetChatBlockDisplayMode("thinkingdisplay", ChatBlockDisplayMode.Collapsed);
            // Shown and Hidden (name-only tool call) start expanded; Collapsed does not.
            bool expandByDefault = mode != ChatBlockDisplayMode.Collapsed;

            var state = new CollapsibleBlockState
            {
                Active = true,
                Name = name,
                Kind = kind,
                EllipsisCount = 1,
                StartedUtc = DateTime.UtcNow
            };

            double fontSize = Document.FontSize > 0 ? Document.FontSize : FontHandler.GetFontSize();

            state.HeaderLabel = new TextBlock { FontSize = fontSize };
            state.HeaderLabel.SetResourceReference(TextBlock.ForegroundProperty, "ChatTextColorBrush");

            state.BodyText = new TextBlock
            {
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
                FontSize = fontSize
            };
            state.BodyText.SetResourceReference(TextBlock.ForegroundProperty, "ChatTextColorBrush");

            state.Expander = new Expander
            {
                Header = state.HeaderLabel,
                Content = state.BodyText,
                IsExpanded = expandByDefault,
                Style = ThinkingExpanderStyle,
                Tag = state
            };
            state.Expander.SetResourceReference(Control.ForegroundProperty, "ChatTextColorBrush");
            state.Expander.Expanded += OnBlockExpanded;
            state.Expander.Collapsed += OnBlockCollapsed;

            Document.Blocks.Add(new BlockUIContainer(state.Expander)
            {
                Margin = new Thickness(0)
            });

            _activeBlock = state;
            state.HeaderLabel.Text = BuildLabelText(state);

            if (state.Collapsed)
                StartEllipsisTimer(state);
        }

        private void EndCollapsibleBlock()
        {
            CollapsibleBlockState state = _activeBlock;
            if (state == null)
                return;

            state.Active = false;
            state.DurationSeconds = Math.Max(0, (int)Math.Round((DateTime.UtcNow - state.StartedUtc).TotalSeconds));
            _activeBlock = null;
            StopEllipsisTimer(state);
            state.HeaderLabel.Text = BuildLabelText(state);

            if (state.BodyText.Text != null)
                state.BodyText.Text = state.BodyText.Text.TrimEnd('\r', '\n');
        }

        private void OnBlockExpanded(object sender, RoutedEventArgs e)
        {
            Expander expander = sender as Expander;
            CollapsibleBlockState state = expander != null ? expander.Tag as CollapsibleBlockState : null;
            if (state == null)
                return;

            StopEllipsisTimer(state);
            state.HeaderLabel.Text = BuildLabelText(state);
        }

        private void OnBlockCollapsed(object sender, RoutedEventArgs e)
        {
            Expander expander = sender as Expander;
            CollapsibleBlockState state = expander != null ? expander.Tag as CollapsibleBlockState : null;
            if (state == null)
                return;

            if (state.Active)
                StartEllipsisTimer(state);
            else
                state.HeaderLabel.Text = BuildLabelText(state);
        }

        private static void StartEllipsisTimer(CollapsibleBlockState state)
        {
            if (state.Timer == null)
            {
                state.Timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(450)
                };
                state.Timer.Tick += (s, e) => OnEllipsisTick(state);
            }

            state.HeaderLabel.Text = BuildLabelText(state);
            if (!state.Timer.IsEnabled)
                state.Timer.Start();
        }

        private static void StopEllipsisTimer(CollapsibleBlockState state)
        {
            if (state.Timer == null)
                return;

            state.Timer.Stop();
        }

        private static void OnEllipsisTick(CollapsibleBlockState state)
        {
            if (!state.Active || !state.Collapsed)
            {
                StopEllipsisTimer(state);
                state.HeaderLabel.Text = BuildLabelText(state);
                return;
            }

            state.EllipsisCount = state.EllipsisCount >= 3 ? 1 : state.EllipsisCount + 1;
            state.HeaderLabel.Text = BuildLabelText(state);
        }

        private static string BuildLabelText(CollapsibleBlockState state)
        {
            if (state.Kind == CollapsibleBlockKind.ToolCall)
            {
                string baseLabel = "tool call: " + (state.Name ?? "tool");
                if (state.Collapsed && state.Active)
                    return baseLabel + new string('.', state.EllipsisCount);
                return baseLabel;
            }

            if (state.Kind == CollapsibleBlockKind.ToolOutput)
            {
                if (state.Collapsed && state.Active)
                    return "tool output" + new string('.', state.EllipsisCount);
                return "tool output";
            }

            if (state.Collapsed && state.Active)
                return "thinking" + new string('.', state.EllipsisCount);

            if (state.Collapsed && !state.Active)
            {
                int seconds = state.DurationSeconds;
                return "thought for " + seconds + " second" + (seconds == 1 ? "" : "s");
            }

            return "thinking";
        }

        /// <summary>
        /// After <c>[tool call]</c>, take the tool name from the rest of the line.
        /// </summary>
        private static string TakeToolCallName(string text, out string name)
        {
            if (string.IsNullOrEmpty(text))
            {
                name = "tool";
                return text;
            }

            int newline = text.IndexOf('\n');
            if (newline < 0)
            {
                name = text.Trim();
                if (name.Length == 0)
                    name = "tool";
                return string.Empty;
            }

            name = text.Substring(0, newline).Trim();
            if (name.Length == 0)
                name = "tool";

            string rest = text.Substring(newline + 1);
            if (rest.Length > 0 && rest[0] == '\r')
                rest = rest.Substring(1);
            return rest;
        }

        private static bool TryFindTag(string text, string[] tags, out int index, out int length)
        {
            index = -1;
            length = 0;

            foreach (string tag in tags)
            {
                int start = 0;
                while (true)
                {
                    int found = text.IndexOf(tag, start, StringComparison.OrdinalIgnoreCase);
                    if (found < 0)
                        break;

                    // Avoid matching an open tag inside its close tag (e.g. "[tool call]" in "[/tool call]").
                    if (found == 0 || text[found - 1] != '/')
                    {
                        if (index < 0 || found < index)
                        {
                            index = found;
                            length = tag.Length;
                        }
                        break;
                    }

                    start = found + 1;
                }
            }

            return index >= 0;
        }
    }
}
