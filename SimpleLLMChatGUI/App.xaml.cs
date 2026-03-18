using System;
using System.Collections.Generic;
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
        private static Dictionary<string, string> _cachedSettings;

        public static Dictionary<string, string> Settings
        {
            get
            {
                if (_cachedSettings == null)
                {
                    LoadSettings();
                }
                return _cachedSettings;
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            LoadSettings();
        }

        public static void LoadSettings()
        {
            if (!File.Exists(ConfigFileName))
            {
                _cachedSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            try
            {
                _cachedSettings = IniFileHandler.LoadIni(ConfigFileName);
            }
            catch
            {
                // If there's an error reading the file, use empty dictionary
                _cachedSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

    }
}
