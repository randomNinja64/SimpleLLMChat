using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace SimpleLLMChatGUI
{
    public partial class MainWindow : Window
    {
        public const string ConfigFileName = "LLMSettings.ini";
        private ProcessHandler processHandler;
        private readonly ImageHandler imageHandler;
        private HotkeyHandler hotkeyHandler;
        private HwndSource source;
        private bool suppressAttachDialog;

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
            StartLLMProcess();
        }

        private void StartLLMProcess()
        {
            processHandler = new ProcessHandler();

            // Subscribe to events
            processHandler.OutputReceived += OnOutputReceived;
            processHandler.ErrorOccurred += OnErrorOccurred;
            processHandler.GenerationComplete += OnGenerationComplete;

            // Start the process
            if (!processHandler.StartProcess("SimpleLLMChatCLI.exe"))
            {
                MessageBox.Show("Failed to start LLM process. Please check if SimpleLLMChatCLI.exe exists.");
            }
        }

        private void OnOutputReceived(string text)
        {
            // Display the text immediately on the UI thread
            Dispatcher.Invoke((Action)(() =>
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
            Dispatcher.Invoke((Action)(() =>
            {
                MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }));
        }

        private void OnGenerationComplete()
        {
            Dispatcher.Invoke((Action)(() =>
            {
                if (IsMarkdownParsingEnabled())
                {
                    MarkdownHandler.processMarkdown(chatOutput);
                }
                SetInputControlsEnabled(true);
                chatInput.Focus();
            }));
        }

        private bool IsMarkdownParsingEnabled()
        {
            if (!File.Exists(ConfigFileName))
                return true; // Default to enabled if config doesn't exist

            try
            {
                var settings = IniFileHandler.LoadIni(ConfigFileName);
                if (settings.TryGetValue("markdownparsing", out string value))
                {
                    return value == "1";
                }
            }
            catch
            {
                // If there's an error reading the file, default to enabled
            }

            return true; // Default to enabled
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

        private void chatInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sendButton.IsEnabled)
            {
                e.Handled = true; // prevent the ding sound
                sendButton_Click(sendButton, new RoutedEventArgs());
            }
        }

        private void ClearChatAndRestart()
        {
            // Queue the clear operation to run after all pending output has been processed
            Dispatcher.Invoke((Action)(() =>
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
                processHandler.Dispose();

            ClearChatAndRestart();
        }

        private void optionsButton_Click(object sender, RoutedEventArgs e)
        {
            var optionsDialog = new Options(processHandler);
            optionsDialog.Owner = this;

            if (optionsDialog.ShowDialog() == true)
            {
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