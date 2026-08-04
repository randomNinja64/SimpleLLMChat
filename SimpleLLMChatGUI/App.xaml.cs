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

        /// <summary>True when LLMSettings.ini does not exist yet (first-run setup needed).</summary>
        public static bool NeedsOnboarding { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            LoadSettings();
        }

        public static void LoadSettings()
        {
            NeedsOnboarding = !File.Exists(ConfigFilePath);
            Config = new ConfigHandler(ConfigFilePath);
        }
    }
}
