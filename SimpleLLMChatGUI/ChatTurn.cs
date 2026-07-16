using System.Windows;
using System.Windows.Documents;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// One chat turn (user or assistant) backed by its own FlowDocument.
    /// </summary>
    public class ChatTurn
    {
        public FlowDocument Document { get; private set; }

        /// <summary>
        /// Block index already processed by MarkdownHandler for this turn's document.
        /// </summary>
        public int MarkdownProcessedBlockCount;

        // False until visible text is appended; used to drop the CLI's
        // inter-turn padding newlines, which would otherwise render as
        // blank first lines in this turn's document.
        private bool _hasContent;

        public ChatTurn()
        {
            Document = new FlowDocument
            {
                PagePadding = new Thickness(0)
            };
        }

        public void AppendText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (!_hasContent)
            {
                // The CLI's own inter-turn padding newlines land unpredictably
                // once each turn is its own document (they may end up here as
                // leading text, or as trailing text on the previous turn, or
                // split across both) - so they're dropped entirely. Turn-to-turn
                // spacing is instead provided deterministically by AddLeadingSeparator.
                text = text.TrimStart('\r', '\n');
                if (text.Length == 0)
                    return;
                _hasContent = true;

                // Always start real content in a fresh paragraph, so a leading
                // separator paragraph (if any) is never overwritten.
                Document.Blocks.Add(new Paragraph());
            }

            new TextRange(Document.ContentEnd, Document.ContentEnd).Text = text;
        }

        /// <summary>
        /// Adds one blank paragraph so this turn renders with a blank line
        /// above it, matching the spacing used between blocks within a turn.
        /// Call before any content is appended, for every turn except the
        /// first one in the conversation.
        /// </summary>
        public void AddLeadingSeparator()
        {
            Document.Blocks.Add(new Paragraph());
        }

        /// <summary>
        /// Removes empty paragraphs left at the end of the document by the
        /// CLI's padding newlines before the next prompt.
        /// </summary>
        public void TrimTrailingBlankParagraphs()
        {
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
    }
}
