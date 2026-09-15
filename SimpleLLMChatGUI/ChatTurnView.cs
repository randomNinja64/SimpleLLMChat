using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// Keeps pixel scrolling on .NET 4: off-screen documents are replaced by their
    /// last measured height. Only small Border placeholders remain in the list.
    /// </summary>
    public sealed class ChatTurnView : Border
    {
        public static readonly DependencyProperty TurnProperty = DependencyProperty.Register(
            "Turn", typeof(ChatTurn), typeof(ChatTurnView), new PropertyMetadata(null, OnTurnChanged));
        public ChatTurn Turn
        {
            get { return (ChatTurn)GetValue(TurnProperty); }
            set { SetValue(TurnProperty, value); }
        }

        private static readonly DependencyProperty PendingCorrectionProperty = DependencyProperty.RegisterAttached(
            "PendingCorrection", typeof(double), typeof(ChatTurnView), new PropertyMetadata(0.0));
        private static readonly DependencyProperty CorrectionQueuedProperty = DependencyProperty.RegisterAttached(
            "CorrectionQueued", typeof(bool), typeof(ChatTurnView), new PropertyMetadata(false));

        private ScrollViewer _scrollViewer;
        private RichTextBox _editor;
        private bool _updating;

        public ChatTurnView()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += delegate { UpdateVisibility(); };
        }

        private static void OnTurnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            ChatTurnView view = (ChatTurnView)sender;
            view.Detach(args.OldValue as ChatTurn);
            view.Height = double.NaN;
            if (view.IsLoaded) view.ShowDocument();
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            if (_scrollViewer != null) _scrollViewer.ScrollChanged -= OnScrollChanged;
            DependencyObject parent = VisualTreeHelper.GetParent(this);
            while (parent != null && !(parent is ScrollViewer))
                parent = VisualTreeHelper.GetParent(parent);
            _scrollViewer = parent as ScrollViewer;
            if (_scrollViewer != null) _scrollViewer.ScrollChanged += OnScrollChanged;
            ShowDocument();
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            if (_scrollViewer != null) _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer = null;
            Detach(Turn);
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs args)
        {
            if (ReferenceEquals(args.OriginalSource, _scrollViewer)) UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (_updating || _scrollViewer == null || Turn == null || !IsLoaded
                || !_scrollViewer.IsAncestorOf(this)) return;
            _updating = true;
            try
            {
                double viewport = _scrollViewer.ViewportHeight;
                if (viewport <= 0) return;
                double top = TransformToAncestor(_scrollViewer).Transform(new Point()).Y;
                // One viewport of prefetch on each side avoids loading at the visible edge.
                bool nearby = top <= viewport * 2 && top + ActualHeight >= -viewport;
                if (nearby || !Turn.IsCompleted)
                    ShowDocument();
                else if (_editor != null && _editor.Selection.IsEmpty && !_editor.IsKeyboardFocusWithin)
                {
                    Height = ActualHeight;
                    Detach(Turn);
                }
            }
            finally { _updating = false; }
        }

        private void ShowDocument()
        {
            if (_editor != null || Turn == null) return;
            double previousHeight = Height;
            bool aboveViewport = _scrollViewer != null && _scrollViewer.IsAncestorOf(this)
                && !double.IsNaN(previousHeight)
                && TransformToAncestor(_scrollViewer).Transform(new Point()).Y + previousHeight <= 0;
            _editor = new RichTextBox
            {
                IsReadOnly = true,
                IsUndoEnabled = false,
                IsDocumentEnabled = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _editor.SetResourceReference(Control.ForegroundProperty, "ChatTextColorBrush");
            Style paragraph = new Style(typeof(Paragraph));
            paragraph.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
            paragraph.Setters.Add(new Setter(Block.PaddingProperty, new Thickness(0)));
            _editor.Resources.Add(typeof(Paragraph), paragraph);
            _editor.Document = Turn.Document;
            Height = double.NaN;
            Child = _editor;
            if (aboveViewport && ActualWidth > 0)
            {
                // Formatting or a font/width change may alter an archived row's height.
                // Correct for rows above the reader, keeping the visible content anchored.
                _editor.Measure(new Size(ActualWidth, double.PositiveInfinity));
                QueueScrollCorrection(_scrollViewer, _editor.DesiredSize.Height - previousHeight);
            }
        }

        private static void QueueScrollCorrection(ScrollViewer scroll, double delta)
        {
            if (Math.Abs(delta) < 0.1) return;
            scroll.SetValue(PendingCorrectionProperty, (double)scroll.GetValue(PendingCorrectionProperty) + delta);
            if ((bool)scroll.GetValue(CorrectionQueuedProperty)) return;
            scroll.SetValue(CorrectionQueuedProperty, true);
            scroll.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                double correction = (double)scroll.GetValue(PendingCorrectionProperty);
                scroll.SetValue(PendingCorrectionProperty, 0.0);
                scroll.SetValue(CorrectionQueuedProperty, false);
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + correction);
            }));
        }

        private void Detach(ChatTurn turn)
        {
            if (_editor != null)
            {
                _editor.Document = new FlowDocument();
                Child = null;
                _editor = null;
            }
            if (turn != null) turn.ReleaseDocument();
        }
    }
}
