using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace SimpleLLMChatGUI
{
    public partial class ChatTurn
    {
        internal enum CollapsibleBlockKind
        {
            Thinking,
            ToolCall,
            ToolOutput
        }

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

        private void StartCollapsibleBlock(string name, CollapsibleBlockKind kind)
        {
            if (!_restoring) _source.Clear();
            _blockKind = kind;
            _blockName = name;
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
            mode = _blockMode ?? mode;
            _blockMode = mode;
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

            if (!_restoring && _source != null) _bodyLength = _source.Length;
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
    }
}
