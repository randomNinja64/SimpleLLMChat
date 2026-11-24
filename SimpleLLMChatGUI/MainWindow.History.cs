using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// The history sidebar: lists saved sessions and opens one on click.
    /// </summary>
    public partial class MainWindow
    {
        private readonly ObservableCollection<ChatHistoryEntry> _historyEntries =
            new ObservableCollection<ChatHistoryEntry>();

        // The session the chat window shows, or null for an unsaved new chat.
        private string _currentChatPath;

        private void historyButton_Toggled(object sender, RoutedEventArgs e)
        {
            bool open = historyButton.IsChecked == true;
            if (open)
                RefreshHistory();

            // Narrowing or widening the chat reflows every turn (chatList_SizeChanged
            // resets page widths), which changes heights. Keep a reader who was
            // following the end of the chat at the end.
            ScrollViewer scroll = FindScrollViewer(chatList);
            bool atEnd = scroll != null && scroll.VerticalOffset >= scroll.ScrollableHeight - 1;

            historyPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

            if (atEnd)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => scroll.ScrollToEnd()));
        }

        private void RefreshHistory()
        {
            if (historyList.ItemsSource == null)
                historyList.ItemsSource = _historyEntries;

            _historyEntries.Clear();
            foreach (ChatHistoryEntry entry in ChatHistoryStore.Read(ChatHistoryStore.ChatsDirectory))
                _historyEntries.Add(entry);

            historyEmptyText.Visibility = _historyEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SelectCurrentChat();
        }

        private void SelectCurrentChat()
        {
            historyList.SelectedItem = null;
            foreach (ChatHistoryEntry entry in _historyEntries)
            {
                if (string.Equals(entry.Path, _currentChatPath, StringComparison.OrdinalIgnoreCase))
                {
                    historyList.SelectedItem = entry;
                    historyList.ScrollIntoView(entry);
                    break;
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
