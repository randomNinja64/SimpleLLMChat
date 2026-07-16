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

            new TextRange(Document.ContentEnd, Document.ContentEnd).Text = text;
        }
    }
}
