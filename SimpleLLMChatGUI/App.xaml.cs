using System;
using System.IO;
using System.Windows;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public const string ConfigFileName = "LLMSettings.ini";
        public const string ColorsFileName = "colors.ini";
        public static ConfigHandler Config;

        /// <summary>Absolute path to LLMSettings.ini next to the executable.</summary>
        public static string ConfigFilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName); }
        }

        /// <summary>Absolute path to colors.ini next to the executable.</summary>
        public static string ColorsFilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ColorsFileName); }
        }

        /// <summary>
        /// The folder the app was started in (a shortcut's "Start in", or the shell's folder),
        /// captured at startup because file dialogs can change the current directory later.
        /// The CLI is started here, so tools use it when no working directory is configured.
        /// </summary>
        public static readonly string StartDirectory = Environment.CurrentDirectory;

        /// <summary>True when LLMSettings.ini does not exist yet (first-run setup needed).</summary>
        public static bool NeedsOnboarding { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Report an unexpected UI-thread error instead of closing the app.
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(args.Exception.Message, "Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            LoadSettings();
        }

        public static void LoadSettings()
        {
            NeedsOnboarding = !File.Exists(ConfigFilePath);
            Config = new ConfigHandler(ConfigFilePath);
        }
    }
}
