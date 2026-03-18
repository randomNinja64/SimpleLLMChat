using System;
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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            LoadSettings();
        }

        public static void LoadSettings()
        {
            Config = new ConfigHandler(ConfigFileName);
        }

    }
}
