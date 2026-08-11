using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace SimpleLLMChatGUI
{
    public partial class MainWindow : Window
    {
        private ProcessHandler processHandler;
        private readonly ImageHandler imageHandler;
        private HotkeyHandler hotkeyHandler;
        private HwndSource source;
        private bool suppressAttachDialog;
        private string _reasoningEffort = "";
        private TokenTracker tokenTracker;
        private ContextMenu reasoningEffortMenu;
        private readonly ObservableCollection<ChatTurn> _chatTurns = new ObservableCollection<ChatTurn>();
        private ChatTurn _streamingTurn;

        private const double ChatTurnGapMin = 8;
        private const double ChatTurnGapMax = 24;

        /// <summary>
        /// Bottom gap between chat turns; scales with font size (~1em + 2), clamped.
        /// </summary>
        public static readonly DependencyProperty ChatTurnItemMarginProperty =
            DependencyProperty.Register(
                nameof(ChatTurnItemMargin),
                typeof(Thickness),
                typeof(MainWindow),
                new PropertyMetadata(new Thickness(0, 0, 0, AppConstants.DefaultChatFontSize + 2)));

        public Thickness ChatTurnItemMargin
        {
            get { return (Thickness)GetValue(ChatTurnItemMarginProperty); }
            set { SetValue(ChatTurnItemMarginProperty, value); }
        }

        private static readonly KeyValuePair<string, string>[] ReasoningEffortOptions =
        {
            new KeyValuePair<string, string>("Default", ""),
            new KeyValuePair<string, string>("None", "none"),
            new KeyValuePair<string, string>("Minimal", "minimal"),
            new KeyValuePair<string, string>("Low", "low"),
            new KeyValuePair<string, string>("Medium", "medium"),
            new KeyValuePair<string, string>("High", "high"),
            new KeyValuePair<string, string>("Extra High", "xhigh"),
        };

        public MainWindow()
        {
            InitializeComponent();

            // Initialize image handler and centralize UI updates via events
            imageHandler = new ImageHandler();

            imageHandler.ImageSelected += (path) =>
            {
                attachButton.ToolTip = "Detach Image";
                attachButton.IsChecked = true;
                attachButton.Background = System.Windows.Media.Brushes.LightBlue;
            };

            imageHandler.ImageDetached += () =>
            {
                attachButton.ToolTip = "Attach Image";
                attachButton.IsChecked = false;
                attachButton.ClearValue(Button.BackgroundProperty);
            };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            chatList.ItemsSource = _chatTurns;
            ChatTurn.ThinkingExpanderStyle = TryFindResource("ThinkingExpanderStyle") as Style;

            FontHandler.ApplyFontToWindow(this);
            FontHandler.ApplyCodeBlockFontFamily();
            LoadAndApplyFontSize();
            LoadAndApplyColors();
            UpdateReasoningEffortLabel();
            BuildReasoningEffortMenu();
            tokenTracker = new TokenTracker(tokenStatusText);

            // Gate send until the CLI is up; leave input/attach usable meanwhile.
            sendButton.IsEnabled = false;

            // After first paint: Process.Start can take 100–1000ms and would white-out Loaded.
            EventHandler onRendered = null;
            onRendered = (s, args) =>
            {
                ContentRendered -= onRendered;
                OnboardingWizardWindow.ShowIfNeeded(this);
                StartLLMProcess();
            };
            ContentRendered += onRendered;
        }

        private void UpdateReasoningEffortLabel()
        {
            string label = ReasoningEffortOptions[0].Key;
            for (int i = 0; i < ReasoningEffortOptions.Length; i++)
            {
                if (string.Equals(ReasoningEffortOptions[i].Value, _reasoningEffort, StringComparison.OrdinalIgnoreCase))
                {
                    label = ReasoningEffortOptions[i].Key;
                    break;
                }
            }
            reasoningEffortLabel.Text = label;
        }

        private void BuildReasoningEffortMenu()
        {
            reasoningEffortMenu = new ContextMenu();
            foreach (KeyValuePair<string, string> option in ReasoningEffortOptions)
            {
                MenuItem item = new MenuItem();
                item.Header = option.Key;
                item.Tag = option.Value;
                item.IsCheckable = true;
                item.Click += reasoningEffortMenuItem_Click;
                reasoningEffortMenu.Items.Add(item);
            }
        }

        private void reasoningEffortLabel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (reasoningEffortMenu == null)
                return;

            foreach (MenuItem item in reasoningEffortMenu.Items)
            {
                string value = item.Tag as string ?? "";
                item.IsChecked = string.Equals(value, _reasoningEffort, StringComparison.OrdinalIgnoreCase);
            }

            reasoningEffortMenu.PlacementTarget = (UIElement)sender;
            reasoningEffortMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            reasoningEffortMenu.IsOpen = true;
        }

        private void reasoningEffortMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;
            if (item == null)
                return;

            _reasoningEffort = item.Tag as string ?? "";
            UpdateReasoningEffortLabel();
            if (processHandler != null && processHandler.IsProcessRunning)
                processHandler.SendReasoningEffort(_reasoningEffort);
        }

        private void StartLLMProcess()
        {
            if (processHandler != null)
            {
                processHandler.Dispose();
                processHandler = null;
            }

            processHandler = new ProcessHandler();

            processHandler.OutputReceived += OnOutputReceived;
            processHandler.ErrorOccurred += OnErrorOccurred;
            processHandler.GenerationComplete += OnGenerationComplete;
            processHandler.ApprovalRequested += OnApprovalRequested;
            processHandler.StatusReceived += OnStatusReceived;

            if (tokenTracker != null)
                tokenTracker.Reset();

            // Start the process
            if (!processHandler.StartProcess("SimpleLLMChatCLI.exe"))
            {
                MessageBox.Show("Failed to start LLM process. Please check if SimpleLLMChatCLI.exe exists.");
                return;
            }

            if (!string.IsNullOrEmpty(_reasoningEffort))
                processHandler.SendReasoningEffort(_reasoningEffort);

            SetInputControlsEnabled(true);
            chatInput.Focus();
        }

        private void OnStatusReceived(int tokens)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (tokenTracker != null)
                    tokenTracker.SetTokens(tokens);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnOutputReceived(string text)
        {
            // BeginInvoke so stdout reading is not gated behind UI paint/scroll work.
            Dispatcher.BeginInvoke((Action)(() =>
            {
                AppendStreamingText(text);
                ScrollChatToEnd();
            }));
        }

        /// <summary>
        /// Each thinking / tool-call block gets its own turn, so the chat list's
        /// item margin separates them the way it separates user and LLM turns.
        /// </summary>
        private void AppendStreamingText(string text)
        {
            string remaining = text;
            while (!string.IsNullOrEmpty(remaining))
            {
                if (_streamingTurn == null)
                    _streamingTurn = AddTurn();

                string leftover = _streamingTurn.AppendText(remaining);
                if (leftover == null)
                    return;

                EndStreamingTurn();
                remaining = leftover;
            }
        }

        private void EndStreamingTurn()
        {
            if (_streamingTurn == null)
                return;

            _streamingTurn.TrimTrailingBlankParagraphs();
            if (!_streamingTurn.HasRenderedContent())
                _chatTurns.Remove(_streamingTurn);
            _streamingTurn = null;
        }

        private void OnErrorOccurred(string errorMessage)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }));
        }

        private bool OnApprovalRequested(string toolName, string arguments)
        {
            bool approved = false;
            // Run after queued stdout updates so the approval block's leading
            // separator can be removed before the (hidden) GUI prompt is shown.
            Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, (Action)(() =>
            {
                if (_streamingTurn != null)
                {
                    _streamingTurn.TrimTrailingBlankParagraphs();
                    // Keep half of the block separator; the CLI emits the
                    // second newline after receiving the approval response.
                    _streamingTurn.AppendText("\r\n");
                }

                string message = ToolApproval.FormatApprovalMessage(toolName, arguments);
                MessageBoxResult result = MessageBox.Show(
                    message,
                    "Tool Call",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
                approved = result == MessageBoxResult.Yes;
            }));
            return approved;
        }

        private void OnGenerationComplete()
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                EndStreamingTurn();
                if (IsMarkdownParsingEnabled())
                    ApplyMarkdown();
                SetInputControlsEnabled(true);
                chatInput.Focus();
            }));
        }

        /// <summary>
        /// A generation now spans several turns; each tracks what it already
        /// rendered, so processed blocks are not revisited.
        /// </summary>
        private void ApplyMarkdown()
        {
            foreach (ChatTurn turn in _chatTurns)
            {
                MarkdownHandler.ProcessMarkdown(
                    turn.Document,
                    ref turn.MarkdownProcessedBlockCount);
            }
        }

        private bool IsMarkdownParsingEnabled()
        {
            return App.Config.GetConfigBool("markdownparsing", true);
        }

        private void LoadAndApplyFontSize()
        {
            int fontSize = FontHandler.GetFontSize();
            FontSize = fontSize;
            ChatTurnItemMargin = new Thickness(0, 0, 0,
                Math.Max(ChatTurnGapMin, Math.Min(ChatTurnGapMax, fontSize + 2)));
            FontHandler.ApplyFontSizeToControl(chatList, fontSize);
            FontHandler.ApplyFontSizeToControl(chatInput, fontSize);
            foreach (ChatTurn turn in _chatTurns)
                turn.ApplyFontSize(fontSize);
        }

        private ChatTurn AddTurn()
        {
            ChatTurn turn = new ChatTurn();
            turn.ApplyFontSize(FontHandler.GetFontSize());
            ApplyDocumentPageWidth(turn);
            _chatTurns.Add(turn);
            return turn;
        }

        private void chatList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            foreach (ChatTurn turn in _chatTurns)
                ApplyDocumentPageWidth(turn);
        }

        private void ApplyDocumentPageWidth(ChatTurn turn)
        {
            if (turn == null || chatList == null)
                return;

            double width = chatList.ActualWidth
                - SystemParameters.VerticalScrollBarWidth
                - 16;
            if (width > 50)
                turn.Document.PageWidth = width;
        }

        private void ScrollChatToEnd()
        {
            if (_chatTurns.Count == 0)
                return;

            // Clear selection before forcing scroll — scrolling while text is
            // highlighted during streaming can freeze the UI thread.
            ClearChatSelection(chatList);

            chatList.ScrollIntoView(_chatTurns[_chatTurns.Count - 1]);

            ScrollViewer scrollViewer = FindScrollViewer(chatList);
            if (scrollViewer != null)
                scrollViewer.ScrollToEnd();
        }

        private static void ClearChatSelection(DependencyObject root)
        {
            RichTextBox richTextBox = root as RichTextBox;
            if (richTextBox != null
                && richTextBox.Selection != null
                && !richTextBox.Selection.IsEmpty)
            {
                TextPointer end = richTextBox.Document.ContentEnd;
                richTextBox.Selection.Select(end, end);
            }
            else
            {
                TextBox textBox = root as TextBox;
                if (textBox != null && textBox.SelectionLength > 0)
                    textBox.Select(0, 0);
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                ClearChatSelection(VisualTreeHelper.GetChild(root, i));
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer viewer)
                return viewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                ScrollViewer child = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (child != null)
                    return child;
            }
            return null;
        }

        private void LoadAndApplyColors()
        {
            if (File.Exists(App.ColorsFileName))
            {
                try
                {
                    var colorSettings = IniFileHandler.LoadIni(App.ColorsFileName);
                    ColorHelper.LoadAndApplyColors(colorSettings);
                }
                catch
                {
                    // On error, do nothing (use system defaults)
                }
            }
        }


        private void SetInputControlsEnabled(bool enabled)
        {
            attachButton.IsEnabled = enabled;
            chatInput.IsEnabled = enabled;
            sendButton.IsEnabled = enabled;
        }

        private void sendButton_Click(object sender, RoutedEventArgs e)
        {
            string userInput = chatInput.Text;

            ChatTurn userTurn = AddTurn();
            userTurn.AppendText("You: " + userInput);
            if (IsMarkdownParsingEnabled())
            {
                MarkdownHandler.ProcessMarkdown(
                    userTurn.Document,
                    ref userTurn.MarkdownProcessedBlockCount);
            }
            _streamingTurn = AddTurn();
            ScrollChatToEnd();

            // Disable input controls while LLM is generating
            SetInputControlsEnabled(false);

            if (processHandler != null && processHandler.IsProcessRunning)
            {
                if (imageHandler?.IsImageAttached == true && !string.IsNullOrEmpty(imageHandler.AttachedImagePath))
                {
                    // Send input with image
                    if (!processHandler.SendInputWithImage(imageHandler.AttachedImagePath, userInput))
                    {
                        MessageBox.Show("Failed to send input with image to the process.");
                        SetInputControlsEnabled(true);
                        return;
                    }

                    // Detach image after sending
                    imageHandler.DetachImage();
                    attachButton.IsChecked = false;
                }
                else
                {
                    // Normal input
                    if (!processHandler.SendInput(userInput))
                    {
                        MessageBox.Show("Failed to send input to the process.");
                        SetInputControlsEnabled(true);
                        return;
                    }
                }

                chatInput.Clear();
            }
            else
            {
                MessageBox.Show("Error: CLI is not running!");
                SetInputControlsEnabled(false);
                chatInput.Clear();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Clean up global hotkey
            if (hotkeyHandler != null)
                hotkeyHandler.Dispose();

            // Always try to clean up the screenshot file
            try
            {
                string screenshotPath = Path.Combine(Path.GetTempPath(), "currentscreen.jpg");
                if (File.Exists(screenshotPath))
                {
                    File.Delete(screenshotPath);
                }
            }
            catch (Exception)
            {
                // Silently fail if we can't delete the file
                // Could log this if needed
            }

            if (processHandler != null)
            {
                processHandler.Dispose();
            }
        }

        // Fired when the attach button is toggled ON
        private void attachButton_Checked(object sender, RoutedEventArgs e)
        {
            if (suppressAttachDialog)
            {
                return;
            }
            if (!imageHandler.SelectImage())
            {
                attachButton.IsChecked = false;
            }
        }

        // Fired when the attach button is toggled OFF
        private void attachButton_Unchecked(object sender, RoutedEventArgs e)
        {
            if (imageHandler != null)
                imageHandler.DetachImage();
        }

        private void chatInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Send on Enter (without Shift), create new line on Shift+Enter
            if (e.Key == Key.Enter && sendButton.IsEnabled)
            {
                if (Keyboard.Modifiers != ModifierKeys.Shift)
                {
                    e.Handled = true; // prevent the ding sound and new line
                    sendButton_Click(sendButton, new RoutedEventArgs());
                }
                // If Shift+Enter, allow default behavior (new line)
            }
        }

        private void ClearChatAndRestart()
        {
            // Queue the clear operation to run after all pending output has been processed.
            // Use BeginInvoke (fire-and-forget) so the UI thread doesn't pump re-entrantly
            // while waiting — that caused intermittent crashes when clicking Clear.
            Dispatcher.BeginInvoke((Action)(() =>
            {
                _chatTurns.Clear();
                _streamingTurn = null;
                SetInputControlsEnabled(false);
                StartLLMProcess();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void clearButton_Click(object sender, RoutedEventArgs e)
        {
            // Kill running process first to stop any new output
            if (processHandler != null)
            {
                processHandler.Dispose();
                processHandler = null;
            }

            ClearChatAndRestart();
        }

        private void optionsButton_Click(object sender, RoutedEventArgs e)
        {
            var optionsDialog = new Options(processHandler);
            optionsDialog.Owner = this;

            if (optionsDialog.ShowDialog() == true)
            {
                App.LoadSettings(); // Reload settings after options dialog saves
                FontHandler.ApplyFontToWindow(this);
                FontHandler.ApplyCodeBlockFontFamily();
                LoadAndApplyFontSize();
                LoadAndApplyColors();
                if (tokenTracker != null)
                    tokenTracker.Refresh();

                if (processHandler != null && processHandler.IsProcessRunning)
                    processHandler.SendReload();
                else
                    ClearChatAndRestart();
            }
        }

        // Desktop Assistant Toggle Event Handlers
        private void DesktopAssistantToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (hotkeyHandler != null)
                hotkeyHandler.Enable();
        }

        private void DesktopAssistantToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (hotkeyHandler != null)
                hotkeyHandler.Disable();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);

            // Setup hotkey handler (Ctrl+Shift+D)
            const int HOTKEY_ID = 1;
            const int MOD_CONTROL = 0x0002;
            const int MOD_SHIFT = 0x0004;
            const int VK_D = 0x44;

            hotkeyHandler = new HotkeyHandler(source, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_D);
            hotkeyHandler.ScreenshotTaken += (path) =>
            {
                Dispatcher.Invoke(new Action(() =>
                {
                    // Set suppressed flag to prevent attach dialog
                    suppressAttachDialog = true;

                    // Bring main window to foreground after screenshot
                    if (WindowState == WindowState.Minimized)
                        WindowState = WindowState.Normal;
                    Activate();
                    Topmost = true;
                    Topmost = false;
                    Focus();

                    // Auto-attach the screenshot; UI is updated via ImageSelected event
                    imageHandler.AttachImageFromPath(path);

                    // Unset suppressed flag after image is attached
                    suppressAttachDialog = false;

                    // Focus the chat textbox
                    chatInput.Focus();
                }));
            };
            hotkeyHandler.ErrorOccurred += (err) =>
            {
                Dispatcher.Invoke(new Action(() =>
                {
                    MessageBox.Show(err, "Screenshot Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }));
            };

            // Add event handler for desktop assistant toggle
            desktopAssistantToggle.Checked += DesktopAssistantToggle_Checked;
            desktopAssistantToggle.Unchecked += DesktopAssistantToggle_Unchecked;
        }
    }
}