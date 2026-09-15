using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// Assigns a <see cref="FlowDocument"/> to a templated <see cref="RichTextBox"/>.
    /// A FlowDocument may only be parented by one RichTextBox at a time (needed for virtualization).
    /// </summary>
    public static class ChatMessageDocumentBehavior
    {
        public static readonly DependencyProperty DocumentProperty =
            DependencyProperty.RegisterAttached(
                "Document",
                typeof(FlowDocument),
                typeof(ChatMessageDocumentBehavior),
                new PropertyMetadata(null, OnDocumentChanged));

        public static void SetDocument(DependencyObject element, FlowDocument value)
        {
            element.SetValue(DocumentProperty, value);
        }

        public static FlowDocument GetDocument(DependencyObject element)
        {
            return (FlowDocument)element.GetValue(DocumentProperty);
        }

        private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            RichTextBox richTextBox = d as RichTextBox;
            if (richTextBox == null)
                return;

            FlowDocument newDocument = e.NewValue as FlowDocument;
            if (newDocument == null)
            {
                richTextBox.Document = new FlowDocument();
                return;
            }

            // Detach from any previous host before reassigning.
            DependencyObject parent = newDocument.Parent;
            if (parent is RichTextBox oldHost && !ReferenceEquals(oldHost, richTextBox))
                oldHost.Document = new FlowDocument();

            richTextBox.Document = newDocument;
        }
    }
}
