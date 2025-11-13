using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;

namespace SimpleLLMChatGUI
{
    public partial class Options : Window, INotifyPropertyChanged
    {
        private string _serverUrl;
        private string _apiKey;
        private string _model;
        private string _sysPrompt;
        private string _assistantName;
        private bool _showToolOutput;
        private ProcessHandler _processHandler;

        public event PropertyChangedEventHandler PropertyChanged;

        private readonly List<string> _availableTools = new List<string> { "download_video", "read_website", "run_shell_command", "run_web_search" };

        public List<string> AvailableTools
        {
            get { return _availableTools; }
        }

        public string ServerURL
        {
            get { return _serverUrl; }
            set { _serverUrl = value; OnPropertyChanged("ServerURL"); }
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

        public bool ShowToolOutput
        {
            get { return _showToolOutput; }
            set { _showToolOutput = value; OnPropertyChanged("ShowToolOutput"); }
        }

        public Options(ProcessHandler processHandler)
        {
            InitializeComponent();
            DataContext = this;

            _processHandler = processHandler;

            // Default values
            ServerURL = "";
            ApiKey = "";
            Model = "";
            SysPrompt = "";
            AssistantName = "";
            ShowToolOutput = true; // Default to showing tool outputs
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
                if (settings.TryGetValue("llmserver", out value)) ServerURL = value;
                if (settings.TryGetValue("apikey", out value)) ApiKey = value;
                if (settings.TryGetValue("model", out value)) Model = value;
                if (settings.TryGetValue("sysprompt", out value)) SysPrompt = value.Trim('"');
                if (settings.TryGetValue("assistantname", out value)) AssistantName = value;
                if (settings.TryGetValue("tools", out value))
                {
                    ApplyToolSelection(value);
                }
                if (settings.TryGetValue("showtooloutput", out value))
                {
                    ShowToolOutput = (value == "1");
                }

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
            var selectedTools = new List<string>();

            foreach (var item in ToolsListBox.SelectedItems)
            {
                var toolName = item as string;
                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    selectedTools.Add(toolName);
                }
            }

            var lines = new List<string>
            {
                "llmserver=" + ServerURL,
                "apikey=" + ApiKey,
                "model=" + Model,
                "sysprompt=\"" + SysPrompt + "\"", // keep quotes around prompt
                "assistantname=" + AssistantName,
                "tools=" + string.Join(",", selectedTools),
                "showtooloutput=" + (ShowToolOutput ? "1" : "0")
            };

            File.WriteAllLines(path, lines.ToArray());
        }

        private void ApplyToolSelection(string toolsValue)
        {
            if (ToolsListBox == null || string.IsNullOrWhiteSpace(toolsValue))
                return;

            var requestedTools = toolsValue.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var requestedTool in requestedTools)
            {
                var trimmed = requestedTool.Trim();
                if (trimmed.Length == 0)
                    continue;

                foreach (var item in ToolsListBox.Items)
                {
                    var toolName = item as string;
                    if (toolName != null &&
                        string.Equals(toolName, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        ToolsListBox.SelectedItems.Add(item);
                        break;
                    }
                }
            }
        }
    }
}
