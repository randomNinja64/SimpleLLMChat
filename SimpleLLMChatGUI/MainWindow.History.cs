using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        // layout.ini keys: what the GUI remembers about its own layout.
        private const string SidebarWidthKey = "historysidebarwidth";
        private const string SidebarOpenKey = "historysidebaropen";

        // The width the user chose by dragging.
        private double _sidebarWidth = SidebarDefaultWidth;

        // Whether the chat was scrolled to its end when a sidebar drag began.
        private bool _chatAtEndBeforeDrag;

        /// <summary>Reopens the sidebar as it was left, at its saved width.</summary>
        private void RestoreSidebar()
        {
            Dictionary<string, string> layout;
            try
            {
                layout = IniFileHandler.LoadIni(App.LayoutFilePath);
            }
            catch (Exception)
            {
                return;   // Unreadable: the defaults, as with colors.ini.
            }

            string text;
            int width;
            if (layout.TryGetValue(SidebarWidthKey, out text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out width))
                _sidebarWidth = Math.Max(SidebarMinWidth, Math.Min(SidebarMaxWidth, width));   // A hand-edited file.

            if (layout.TryGetValue(SidebarOpenKey, out text) && text.Trim() == "1")
                historyButton.IsChecked = true;   // Opens it through historyButton_Toggled.
        }

        private void SaveLayout()
        {
            try
            {
                File.WriteAllLines(App.LayoutFilePath, new[]
                {
                    SidebarOpenKey + "=" + (historyButton.IsChecked == true ? "1" : "0"),
                    SidebarWidthKey + "=" + ((int)_sidebarWidth).ToString(CultureInfo.InvariantCulture)
                });
            }
            catch (Exception)
            {
                // Not remembered this time.
            }
        }

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
            SaveLayout();
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
            SaveLayout();
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
            bool enabled = IsHistoryEnabled();
            if (enabled && _currentWorkspace != null)
            {
                foreach (ChatHistoryEntry entry in ChatHistoryStore.Read(ChatWorkspace.DirectoryFor(_currentWorkspace)))
                    _historyEntries.Add(entry);
            }

            historyEmptyText.Text = enabled ? "(empty)" : "Chat history disabled.";
            // Not "(empty)" before the CLI has said which workspace it is in (the
            // sidebar can open at startup, ahead of the CLI).
            bool known = !enabled || _currentWorkspace != null;
            historyEmptyText.Visibility = _historyEntries.Count == 0 && known ? Visibility.Visible : Visibility.Collapsed;
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
        // the list doesn't restart the CLI for every row passed. Delete acts on the
        // selected row, as in Explorer.
        private void historyList_KeyDown(object sender, KeyEventArgs e)
        {
            ChatHistoryEntry entry = historyList.SelectedItem as ChatHistoryEntry;
            if (e.Key == Key.Enter)
                OpenHistoryEntry(entry);
            else if (e.Key == Key.Delete && entry != null)
                DeleteChat(entry);
            else
                return;
            e.Handled = true;
        }

        private static bool IsHistoryEnabled()
        {
            return App.Config.Get(AppSettings.ChatHistoryLimit) > 0;
        }

        // Built when it opens: a chat's actions when right-clicking a chat, and
        // Import anywhere in the list.
        private void historyList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            DependencyObject node = e.OriginalSource as DependencyObject;
            while (node != null && !(node is ListBoxItem))
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            ChatHistoryEntry entry = node != null ? ((ListBoxItem)node).DataContext as ChatHistoryEntry : null;

            ContextMenu menu = historyList.ContextMenu;
            menu.Items.Clear();
            if (entry != null)
            {
                menu.Items.Add(MenuItemFor("Export...", () => ExportChat(entry)));
                menu.Items.Add(MenuItemFor("Delete", () => DeleteChat(entry)));
                menu.Items.Add(new Separator());
            }
            MenuItem import = MenuItemFor("Import...", ImportChat);
            import.IsEnabled = IsHistoryEnabled();
            menu.Items.Add(import);
        }

        private static MenuItem MenuItemFor(string header, Action action)
        {
            MenuItem item = new MenuItem { Header = header };
            item.Click += (s, a) => action();
            return item;
        }

        // A copy of the saved file: auto-save keeps it current after every turn.
        private void ExportChat(ChatHistoryEntry entry)
        {
            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog();
            dialog.Filter = "Chat (*.json)|*.json";
            dialog.FileName = SafeFileName(entry.Title) + ".json";
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                File.Copy(entry.Path, dialog.FileName, true);
            }
            catch (Exception ex)
            {
                ShowError("Failed to export chat: " + ex.Message);
            }
        }

        // Copies the chat into the current workspace (the CLI's /import), so it
        // shows in this workspace's history; the original file is untouched.
        private void ImportChat()
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "Chat (*.json)|*.json";
            if (dialog.ShowDialog(this) != true)
                return;

            // The copy's path arrives with the CLI's first session message.
            RestartChat("/import \"" + dialog.FileName + "\"", null);
        }

        private void DeleteChat(ChatHistoryEntry entry)
        {
            MessageBoxResult answer = MessageBox.Show(
                "Delete \"" + entry.Title + "\"? This can't be undone.",
                "Delete Chat", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            // Stop the CLI first, or its next auto-save would write the chat back.
            if (entry.IsCurrent)
                RestartChat(null, null);

            try
            {
                File.Delete(entry.Path);
            }
            catch (Exception ex)
            {
                ShowError("Failed to delete chat: " + ex.Message);
            }
            RefreshHistory();
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private static string SafeFileName(string title)
        {
            string name = title ?? "chat";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name.Trim().Length == 0 ? "chat" : name.Trim();
        }

        private void OpenHistoryEntry(ChatHistoryEntry entry)
        {
            if (entry == null
                || string.Equals(entry.Path, _currentChatPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (!File.Exists(entry.Path))
            {
                ShowError("Chat file not found.");
                RefreshHistory();
                return;
            }

            LoadChat(entry.Path);
        }
    }
}
