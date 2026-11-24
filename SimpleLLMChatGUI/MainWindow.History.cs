using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// The history sidebar: lists the current workspace's saved sessions and opens one on click.
    /// </summary>
    public partial class MainWindow
    {
        private readonly ObservableCollection<ChatHistoryEntry> _historyEntries =
            new ObservableCollection<ChatHistoryEntry>();

        // The session the chat window shows, or null for an unsaved new chat.
        private string _currentChatPath;

        // The folder the CLI's tools run in, from its ready line; the sidebar lists
        // this workspace's chats only. Null until the CLI first reports it.
        private string _currentWorkspace;

        // At most 250 the sidebar, handle (5) and chat (150 minimum) fit the smallest
        // window (MinWidth 415, less margins), so window resizes never need to re-fit it.
        private const double SidebarMinWidth = 100;
        private const double SidebarMaxWidth = 250;
        private const double SidebarDefaultWidth = 140;
        private const string SidebarWidthKey = "historysidebarwidth";

        // The width the user chose by dragging, kept in layout.ini. -1 until first read.
        private double _sidebarWidth = -1;

        // Whether the chat was scrolled to its end when a sidebar drag began.
        private bool _chatAtEndBeforeDrag;

        private void historyButton_Toggled(object sender, RoutedEventArgs e)
        {
            bool open = historyButton.IsChecked == true;
            if (open)
                RefreshHistory();

            bool atEnd = IsChatAtEnd();
            Visibility visibility = open ? Visibility.Visible : Visibility.Collapsed;
            historyPanel.Visibility = visibility;
            historySplitter.Visibility = visibility;
            ApplySidebarWidth();
            if (atEnd)
                ScrollChatToEndAfterLayout();
        }

        private void ApplySidebarWidth()
        {
            if (historyPanel.Visibility != Visibility.Visible)
            {
                historyColumn.MinWidth = 0;
                historyColumn.MaxWidth = 0;
                historyColumn.Width = new GridLength(0);
                return;
            }

            if (_sidebarWidth < 0)
                _sidebarWidth = LoadSidebarWidth();

            // The limits also bound the drag.
            historyColumn.MaxWidth = SidebarMaxWidth;
            historyColumn.MinWidth = SidebarMinWidth;
            historyColumn.Width = new GridLength(_sidebarWidth);
        }

        private void historySplitter_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _chatAtEndBeforeDrag = IsChatAtEnd();
        }

        private void historySplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _sidebarWidth = Math.Round(historyColumn.Width.Value);
            if (_chatAtEndBeforeDrag)
                ScrollChatToEndAfterLayout();

            try
            {
                File.WriteAllLines(App.LayoutFilePath, new[]
                {
                    SidebarWidthKey + "=" + ((int)_sidebarWidth).ToString(CultureInfo.InvariantCulture)
                });
            }
            catch (Exception)
            {
                // Not remembered this time; the sidebar keeps the width for this run.
            }
        }

        private static double LoadSidebarWidth()
        {
            try
            {
                string text;
                int width;
                if (IniFileHandler.LoadIni(App.LayoutFilePath).TryGetValue(SidebarWidthKey, out text)
                    && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out width))
                    return Math.Max(SidebarMinWidth, Math.Min(SidebarMaxWidth, width));   // A hand-edited file.
            }
            catch (Exception)
            {
                // Unreadable: the default, as with colors.ini.
            }
            return SidebarDefaultWidth;
        }

        // Narrowing or widening the chat reflows every turn (chatList_SizeChanged
        // resets page widths), which changes heights. A reader who was following
        // the end of the chat is kept at the end.
        private bool IsChatAtEnd()
        {
            ScrollViewer scroll = FindScrollViewer(chatList);
            return scroll != null && scroll.VerticalOffset >= scroll.ScrollableHeight - 1;
        }

        private void ScrollChatToEndAfterLayout()
        {
            ScrollViewer scroll = FindScrollViewer(chatList);
            if (scroll != null)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => scroll.ScrollToEnd()));
        }

        // The CLI saved the session after a turn: a new chat now has its file,
        // and its title and position in the list may have changed.
        private void OnSessionSaved(string path)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _currentChatPath = path;
                if (historyPanel.Visibility == Visibility.Visible)
                    RefreshHistory();
            }));
        }

        // Sent with every ready line. It changes when a loaded chat switches to the
        // workspace it was made in, and back on New Chat.
        private void OnWorkspaceReceived(string workspace)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ChatWorkspace.SamePath(workspace, _currentWorkspace))
                    return;
                _currentWorkspace = workspace;
                if (historyPanel.Visibility == Visibility.Visible)
                    RefreshHistory();
            }));
        }

        private void RefreshHistory()
        {
            if (historyList.ItemsSource == null)
                historyList.ItemsSource = _historyEntries;

            _historyEntries.Clear();
            bool enabled = App.Config.Get(AppSettings.ChatHistoryLimit) > 0;
            if (enabled && _currentWorkspace != null)
            {
                foreach (ChatHistoryEntry entry in ChatHistoryStore.Read(ChatWorkspace.DirectoryFor(_currentWorkspace)))
                    _historyEntries.Add(entry);
            }

            historyEmptyText.Text = enabled ? "(empty)" : "Chat history disabled.";
            historyEmptyText.Visibility = _historyEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SelectCurrentChat();
        }

        // Marks the open chat (its own highlight, kept while arrow keys move the
        // selection) and starts the selection there for keyboard use.
        private void SelectCurrentChat()
        {
            historyList.SelectedItem = null;
            foreach (ChatHistoryEntry entry in _historyEntries)
            {
                entry.IsCurrent = string.Equals(entry.Path, _currentChatPath, StringComparison.OrdinalIgnoreCase);
                if (entry.IsCurrent)
                {
                    historyList.SelectedItem = entry;
                    historyList.ScrollIntoView(entry);
                }
            }
        }

        private void historyItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ListBoxItem item = sender as ListBoxItem;
            if (item != null)
                OpenHistoryEntry(item.DataContext as ChatHistoryEntry);
        }

        // Opening is on click or Enter, not selection change, so arrowing through
        // the list doesn't restart the CLI for every row passed.
        private void historyList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;
            OpenHistoryEntry(historyList.SelectedItem as ChatHistoryEntry);
            e.Handled = true;
        }

        private void OpenHistoryEntry(ChatHistoryEntry entry)
        {
            if (entry == null
                || string.Equals(entry.Path, _currentChatPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (!File.Exists(entry.Path))
            {
                MessageBox.Show("Chat file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                RefreshHistory();
                return;
            }

            LoadChat(entry.Path);
        }
    }
}
