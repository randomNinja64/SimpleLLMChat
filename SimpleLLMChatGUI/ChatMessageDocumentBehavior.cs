using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// Assigns a <see cref="FlowDocument"/> to a templated <see cref="RichTextBox"/>.
    /// A FlowDocument may only be parented by one RichTextBox at a time, so this
    /// detaches on unload / recycle and reattaches on load (needed for virtualization).
    /// Completed off-screen turns hibernate to source text while detached.
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

            richTextBox.Loaded -= OnRichTextBoxLoaded;
            richTextBox.Unloaded -= OnRichTextBoxUnloaded;
            richTextBox.Loaded += OnRichTextBoxLoaded;
            richTextBox.Unloaded += OnRichTextBoxUnloaded;

            HibernateOwner(e.OldValue as FlowDocument, richTextBox.DataContext as ChatTurn);

            if (!richTextBox.IsLoaded)
            {
                DetachIfHosting(richTextBox, e.OldValue as FlowDocument);
                return;
            }

            ChatTurn turn = richTextBox.DataContext as ChatTurn;
            if (turn != null)
                turn.RestoreIfHibernated();

            AttachDocument(richTextBox, GetDocument(richTextBox) ?? (turn != null ? turn.Document : null));
        }

        private static void OnRichTextBoxLoaded(object sender, RoutedEventArgs e)
        {
            RichTextBox richTextBox = sender as RichTextBox;
            if (richTextBox == null)
                return;

            ChatTurn turn = richTextBox.DataContext as ChatTurn;
            if (turn != null)
                turn.RestoreIfHibernated();

            AttachDocument(richTextBox, GetDocument(richTextBox) ?? (turn != null ? turn.Document : null));
        }

        private static void OnRichTextBoxUnloaded(object sender, RoutedEventArgs e)
        {
            RichTextBox richTextBox = sender as RichTextBox;
            if (richTextBox == null)
                return;

            ChatTurn turn = richTextBox.DataContext as ChatTurn;
            DetachIfHosting(richTextBox, GetDocument(richTextBox) ?? (turn != null ? turn.Document : null));
            if (turn != null)
                turn.Hibernate();
        }

        private static void HibernateOwner(FlowDocument oldDocument, ChatTurn currentTurn)
        {
            ChatTurn oldTurn = oldDocument != null ? oldDocument.Tag as ChatTurn : null;
            if (oldTurn == null || ReferenceEquals(oldTurn, currentTurn))
                return;

            oldTurn.Hibernate();
        }

        private static void AttachDocument(RichTextBox richTextBox, FlowDocument document)
        {
            if (document == null)
            {
                EnsurePlaceholder(richTextBox);
                return;
            }

            DependencyObject parent = document.Parent;
            if (ReferenceEquals(parent, richTextBox))
                return;

            if (parent is RichTextBox oldHost)
                EnsurePlaceholder(oldHost);

            richTextBox.Document = document;
        }

        private static void DetachIfHosting(RichTextBox richTextBox, FlowDocument document)
        {
            if (document == null)
                return;
            if (!ReferenceEquals(richTextBox.Document, document))
                return;

            EnsurePlaceholder(richTextBox);
        }

        private static void EnsurePlaceholder(RichTextBox richTextBox)
        {
            FlowDocument current = richTextBox.Document;
            if (IsPlaceholder(current) && ReferenceEquals(current.Parent, richTextBox))
                return;

            richTextBox.Document = CreatePlaceholder();
        }

        private static bool IsPlaceholder(FlowDocument document)
        {
            return document != null
                && document.Tag as string == PlaceholderTag
                && document.Blocks.Count == 0;
        }

        private static FlowDocument CreatePlaceholder()
        {
            return new FlowDocument
            {
                PagePadding = new Thickness(0),
                Tag = PlaceholderTag
            };
        }

        private const string PlaceholderTag = "chat-doc-placeholder";
    }
}
