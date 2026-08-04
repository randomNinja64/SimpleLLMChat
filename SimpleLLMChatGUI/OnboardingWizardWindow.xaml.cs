using System;
using System.Windows;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// First-run wizard for essential System settings when LLMSettings.ini is missing.
    /// </summary>
    public partial class OnboardingWizardWindow : Window
    {
        private static bool _isShowing;
        private static bool _suppressForSession;

        public OnboardingWizardWindow()
        {
            InitializeComponent();
            FontHandler.ApplyFontToWindow(this);
        }

        /// <summary>
        /// Shows the onboarding wizard if config is missing. No-op when already configured,
        /// already open, or suppressed for this session after Cancel.
        /// </summary>
        public static void ShowIfNeeded(Window owner)
        {
            if (!App.NeedsOnboarding || _isShowing || _suppressForSession)
                return;

            _isShowing = true;
            try
            {
                var wizard = new OnboardingWizardWindow();
                if (owner != null)
                    wizard.Owner = owner;
                wizard.ShowDialog();

                if (App.NeedsOnboarding)
                    _suppressForSession = true;
            }
            finally
            {
                _isShowing = false;
            }
        }

        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = ApiKeyPasswordBox.Password ?? string.Empty;
            string server = ServerTextBox.Text ?? string.Empty;
            string model = ModelTextBox.Text ?? string.Empty;

            int contextWindowSize = 0;
            int parsed;
            if (int.TryParse((ContextWindowSizeTextBox.Text ?? string.Empty).Trim(), out parsed) && parsed > 0)
                contextWindowSize = parsed;

            SettingsIniWriter.WriteInitialConfig(
                App.ConfigFilePath,
                apiKey,
                server,
                model,
                string.Empty,
                contextWindowSize);

            App.LoadSettings();

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
