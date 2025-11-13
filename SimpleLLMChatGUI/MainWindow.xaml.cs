using System;
using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Win32;
using System.Windows.Input;
using System.Windows.Interop;
using System.IO;

namespace SimpleLLMChatGUI
{
    public partial class MainWindow : Window
    {
        private ProcessHandler processHandler;
        private ImageHandler imageHandler;
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

        private void sendButton_Click(object sender, RoutedEventArgs e)
        {
            string userInput = chatInput.Text;

            chatOutput.AppendText("You: " + userInput + "\r\n");
            chatOutput.ScrollToEnd();

            if (processHandler.IsProcessRunning)
            {
                if (imageHandler != null && imageHandler.IsImageAttached && !string.IsNullOrEmpty(imageHandler.AttachedImagePath))
                {
                    // Send input with image
                    if (!processHandler.SendInputWithImage(imageHandler.AttachedImagePath, userInput))
                    {
                        MessageBox.Show("Failed to send input with image to the process.");
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
                    }
                }

                chatInput.Clear();
            }
            else
            {
                MessageBox.Show("Error: CLI is not running!");
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
            if (e.Key == Key.Enter)
            {
                e.Handled = true; // prevent the ding sound
                sendButton_Click(sendButton, new RoutedEventArgs());
            }
        }

        private void clearButton_Click(object sender, RoutedEventArgs e)
        {
            // Send clear command to CLI app
            if (processHandler != null && processHandler.IsProcessRunning)
            {
                if (!processHandler.SendInput("clear"))
                {
                    MessageBox.Show("Failed to send clear command to the process.");
                }
            }
            else
            {
                MessageBox.Show("Error: CLI is not running!");
            }

            // Clear the chat output rich text box immediately
            chatOutput.Document.Blocks.Clear();
        }

        private void optionsButton_Click(object sender, RoutedEventArgs e)
        {
            var optionsDialog = new Options(processHandler);
            optionsDialog.Owner = this;

            if (optionsDialog.ShowDialog() == true)
            {
                //Clear output
                chatOutput.Document.Blocks.Clear();

                StartLLMProcess();
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