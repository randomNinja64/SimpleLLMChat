using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;

namespace SimpleLLMChatGUI
{
    public partial class Options : Window, INotifyPropertyChanged
    {
        private string _host;
        private string _port;
        private string _apiKey;
        private string _model;
        private string _sysPrompt;
        private string _assistantName;
        private ProcessHandler _processHandler;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Host
        {
            get { return _host; }
            set { _host = value; OnPropertyChanged("Host"); }
        }

        public string Port
        {
            get { return _port; }
            set { _port = value; OnPropertyChanged("Port"); }
        }

        public string ApiKey
        {
            get { return _apiKey; }
            set { _apiKey = value; OnPropertyChanged("ApiKey"); }
        }

        public string Model
        {
            get { return _model; }
            set { _model = value; OnPropertyChanged("Model"); }
        }

        public string SysPrompt
        {
            get { return _sysPrompt; }
            set { _sysPrompt = value; OnPropertyChanged("SysPrompt"); }
        }

        public string AssistantName
        {
            get { return _assistantName; }
            set { _assistantName = value; OnPropertyChanged("AssistantName"); }
        }

        public Options(ProcessHandler processHandler)
        {
            InitializeComponent();
            DataContext = this;

            _processHandler = processHandler;

            // Default values
            Host = "";
            Port = "";
            ApiKey = "";
            Model = "";
            SysPrompt = "";
            AssistantName = "";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ApiKey = ApiKeyPasswordBox.Password;

            string configFile = "LLMSettings.ini";
            SaveIni(configFile);

            // Kill running process
            if (_processHandler != null)
                _processHandler.Dispose();

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string configFile = "LLMSettings.ini";

            if (File.Exists(configFile))
            {
                var settings = LoadIni(configFile);

                string value;
                if (settings.TryGetValue("host", out value)) Host = value;
                if (settings.TryGetValue("port", out value)) Port = value;
                if (settings.TryGetValue("apikey", out value)) ApiKey = value;
                if (settings.TryGetValue("model", out value)) Model = value;
                if (settings.TryGetValue("sysprompt", out value)) SysPrompt = value.Trim('"');
                if (settings.TryGetValue("assistantname", out value)) AssistantName = value;

                // Sync password box manually (not bound)
                ApiKeyPasswordBox.Password = ApiKey;
            }
            else
            {
                MessageBox.Show("INI file not found: " + configFile, "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private Dictionary<string, string> LoadIni(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#") || !trimmed.Contains("="))
                    continue;

                string[] parts = trimmed.Split(new char[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim();
                    string val = parts[1].Trim();
                    dict[key] = val;
                }
            }

            return dict;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }

        private void SaveIni(string path)
        {
            var lines = new List<string>
            {
                "host=" + Host,
                "port=" + Port,
                "apikey=" + ApiKey,
                "model=" + Model,
                "sysprompt=\"" + SysPrompt + "\"", // keep quotes around prompt
                "assistantname=" + AssistantName
            };

            File.WriteAllLines(path, lines.ToArray());
        }
    }
}
