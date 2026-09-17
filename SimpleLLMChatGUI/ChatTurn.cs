using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// A text-backed chat turn with a releasable rendered document.
    /// </summary>
    public class ChatTurn
    {
        /// <summary>
        /// Classic +/- expander style provided by <c>MainWindow</c>.
        /// </summary>
        public static Style ThinkingExpanderStyle { get; set; }

        private FlowDocument _document;
        private StringBuilder _source = new StringBuilder();
        private string _completedSource;
        private bool _completed;
        private bool _markdown;
        private double _fontSize = 12;
        private double _pageWidth = double.NaN;
        private bool? _expanded;
        private int _durationSeconds;
        private ChatBlockDisplayMode? _blockMode;
        private CollapsibleBlockKind? _blockKind;
        private string _blockName;
        private int _bodyLength;
        private bool _restoring;
        private bool _hasContent;
        private CollapsibleBlockState _activeBlock;

        public bool IsCompleted { get { return _completed; } }
        public bool HasDocument { get { return _document != null; } }
        public string SourceText { get { return _completedSource ?? _source.ToString(); } }
        public int MarkdownProcessedBlockCount;

        public FlowDocument Document
        {
            get
            {
                if (_document == null)
                {
                    _document = new FlowDocument { PagePadding = new Thickness(0) };
                    _hasContent = false;
                    MarkdownProcessedBlockCount = 0;
                    _restoring = true;
                    try
                    {
                        if (_blockKind.HasValue)
                        {
                            StartCollapsibleBlock(_blockName, _blockKind.Value);
                            _activeBlock.BodyText.Text = SourceText.Substring(0, _bodyLength);
                            EndCollapsibleBlock();
                            AppendPlain(SourceText.Substring(_bodyLength));
                        }
                        else if (SourceText.Length != 0)
                        {
                            _document.Blocks.Add(new Paragraph());
                            AppendPlain(SourceText);
                        }
                        _hasContent = SourceText.Length != 0 || _blockKind.HasValue;
                    }
                    finally { _restoring = false; }
                    if (_completed) TrimTrailingBlankParagraphs();
                    foreach (Block block in _document.Blocks)
                    {
                        BlockUIContainer container = block as BlockUIContainer;
                        Expander expander = container != null ? container.Child as Expander : null;
                        if (expander == null) continue;
                        CollapsibleBlockState state = (CollapsibleBlockState)expander.Tag;
                        state.DurationSeconds = _durationSeconds;
                        if (_expanded.HasValue) expander.IsExpanded = _expanded.Value;
                        state.HeaderLabel.Text = BuildLabelText(state);
                    }
                    ApplyFontSize(_fontSize);
                    SetPageWidth(_pageWidth);
                    if (_markdown) ProcessMarkdown();
                }
                return _document;
            }
        }

        public ChatTurn()
        {
            _document = new FlowDocument
            {
                PagePadding = new Thickness(0)
            };
        }

        public void Complete()
        {
            if (_completed) return;
            TrimTrailingBlankParagraphs();
            _completed = true;
            _completedSource = _source.ToString();
            _source = null;
        }

        public void ProcessMarkdown()
        {
            _markdown = true;
            if (_document != null)
                MarkdownHandler.ProcessMarkdown(_document, ref MarkdownProcessedBlockCount);
        }

        public void SetPageWidth(double width)
        {
            _pageWidth = width;
            if (_document != null) _document.PageWidth = width;
        }

        public void ReleaseDocument()
        {
            if (!_completed || _document == null || _document.Parent != null) return;
            foreach (Block block in _document.Blocks)
            {
                BlockUIContainer container = block as BlockUIContainer;
                Expander expander = container != null ? container.Child as Expander : null;
                if (expander == null) continue;
                CollapsibleBlockState state = (CollapsibleBlockState)expander.Tag;
                _expanded = expander.IsExpanded;
                _durationSeconds = state.DurationSeconds;
                StopEllipsisTimer(state);
            }
            _activeBlock = null;
            _document = null;
        }

        public string AppendText(string text)
        {
            if (_completed) throw new InvalidOperationException("Cannot append to a completed turn.");
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
                        AppendBody(remaining);
                        return null;
                    }

                    if (closeIndex > 0)
                        AppendBody(remaining.Substring(0, closeIndex));

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

            bool removed = false;
            while (Document.Blocks.Count > 1)
            {
                Paragraph paragraph = Document.Blocks.LastBlock as Paragraph;
                if (paragraph == null)
                    break;

                string text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
                if (text.Trim().Length != 0)
                    break;

                Document.Blocks.Remove(paragraph);
                removed = true;
            }

            // Approval prompts can trim padding before streaming resumes. Keep the
            // source in sync so restoring history cannot reintroduce that padding.
            if (removed && _source != null)
            {
                string source = _source.ToString();
                int end = source.Length;
                while (end > 0)
                {
                    int start = end;
                    while (start > 0 && source[start - 1] != '\r' && source[start - 1] != '\n') start--;
                    if (source.Substring(start, end - start).Trim().Length != 0) break;
                    end = start;
                    while (end > 0 && (source[end - 1] == '\r' || source[end - 1] == '\n')) end--;
                }
                _source.Length = Math.Max(_blockKind.HasValue ? _bodyLength : 0, end);
            }
        }

        /// <summary>
        /// Applies font size to the document, markdown headers, and collapsible expanders.
        /// </summary>
        public void ApplyFontSize(double fontSize)
        {
            _fontSize = fontSize;
            if (_document == null) return;
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

            if (!_restoring) _source.Append(text);
            new TextRange(Document.ContentEnd, Document.ContentEnd).Text = text;
        }

        private void AppendBody(string text)
        {
            _source.Append(text);
            _activeBlock.BodyText.Text += text;
        }
    }
}
