using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        private bool suppressReasoningEffortChange;
        private string _reasoningEffort = "";
        private TokenTracker tokenTracker;

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
            FontHandler.ApplyFontToWindow(this);
            LoadAndApplyFontSize();
            LoadAndApplyColors();
            LoadReasoningEffortSelection();
            tokenTracker = new TokenTracker(tokenStatusText);
            StartLLMProcess();
        }

        private void LoadReasoningEffortSelection()
        {
            suppressReasoningEffortChange = true;
            try
            {
                reasoningEffortComboBox.Items.Clear();
                foreach (KeyValuePair<string, string> option in ReasoningEffortOptions)
                    reasoningEffortComboBox.Items.Add(option.Key);

                int selectedIndex = 0;
                for (int i = 0; i < ReasoningEffortOptions.Length; i++)
                {
                    if (string.Equals(ReasoningEffortOptions[i].Value, _reasoningEffort, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }

                reasoningEffortComboBox.SelectedIndex = selectedIndex;
            }
            finally
            {
                suppressReasoningEffortChange = false;
            }
        }

        private void reasoningEffortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressReasoningEffortChange || reasoningEffortComboBox.SelectedIndex < 0)
                return;

            _reasoningEffort = ReasoningEffortOptions[reasoningEffortComboBox.SelectedIndex].Value;
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
        }

        private void OnStatusReceived(int tokens)
        {
            // Fire-and-forget: never block the pipe reader on the UI thread.
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
                // Add a blank line before the first bot response after clearing
                if (chatOutput.Document.Blocks.Count == 1)
                {
                    chatOutput.AppendText("\r\n");
                }

                chatOutput.AppendText(text);
                chatOutput.ScrollToEnd();
            }));
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
            Dispatcher.Invoke((Action)(() =>
            {
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
                if (IsMarkdownParsingEnabled())
                {
                    MarkdownHandler.ProcessMarkdown(chatOutput);
                }
                SetInputControlsEnabled(true);
                chatInput.Focus();
            }));
        }

        private bool IsMarkdownParsingEnabled()
        {
            return App.Config.GetConfigBool("markdownparsing", true);
        }

        private void LoadAndApplyFontSize()
        {
            int fontSize = FontHandler.GetFontSize();
            FontHandler.ApplyFontSizeToControl(chatOutput, fontSize);
            FontHandler.ApplyFontSizeToControl(chatInput, fontSize);
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

            chatOutput.AppendText("You: " + userInput + "\r\n");
            chatOutput.ScrollToEnd();

            // Disable input controls while LLM is generating
            SetInputControlsEnabled(false);

            if (processHandler.IsProcessRunning)
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
                SetInputControlsEnabled(true);
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
                chatOutput.Document.Blocks.Clear();
                StartLLMProcess();
                SetInputControlsEnabled(true);
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
                LoadAndApplyFontSize();
                LoadAndApplyColors();
                if (tokenTracker != null)
                    tokenTracker.Refresh();
                processHandler = null; // Options disposes the process on save
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