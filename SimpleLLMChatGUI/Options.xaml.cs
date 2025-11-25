using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;

namespace SimpleLLMChatGUI
{
    public partial class Options : Window, INotifyPropertyChanged
    {
        private const string ConfigFileName = "LLMSettings.ini";

        private string _serverUrl;
        private string _apiKey;
        private string _model;
        private string _sysPrompt;
        private string _assistantName;
        private bool _showToolOutput;
        private int _maxContentLength;
        private bool _markdownParsing;
        private string _searxngInstance;
        private ProcessHandler _processHandler;

        public event PropertyChangedEventHandler PropertyChanged;

        private readonly List<string> _availableTools = new List<string> { "copy_file", "delete_file", "download_file", "download_video", "extract_file", "list_directory", "move_file", "read_file", "read_website", "run_python_script", "run_shell_command", "run_web_search", "write_file" };

        public List<string> AvailableTools
        {
            get { return _availableTools; }
        }

        public string ServerURL
        {
            get { return _serverUrl; }
            set { _serverUrl = value; OnPropertyChanged(nameof(ServerURL)); }
        }

        public string ApiKey
        {
            get { return _apiKey; }
            set { _apiKey = value; OnPropertyChanged(nameof(ApiKey)); }
        }

        public string Model
        {
            get { return _model; }
            set { _model = value; OnPropertyChanged(nameof(Model)); }
        }

        public string SysPrompt
        {
            get { return _sysPrompt; }
            set { _sysPrompt = value; OnPropertyChanged(nameof(SysPrompt)); }
        }

        public string AssistantName
        {
            get { return _assistantName; }
            set { _assistantName = value; OnPropertyChanged(nameof(AssistantName)); }
        }

        public bool ShowToolOutput
        {
            get { return _showToolOutput; }
            set { _showToolOutput = value; OnPropertyChanged(nameof(ShowToolOutput)); }
        }

        public int MaxContentLength
        {
            get { return _maxContentLength; }
            set { _maxContentLength = value; OnPropertyChanged(nameof(MaxContentLength)); }
        }

        public bool MarkdownParsing
        {
            get { return _markdownParsing; }
            set { _markdownParsing = value; OnPropertyChanged(nameof(MarkdownParsing)); }
        }

        public string SearxNGInstance
        {
            get { return _searxngInstance; }
            set { _searxngInstance = value; OnPropertyChanged(nameof(SearxNGInstance)); }
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
            MaxContentLength = 8000; // Default to 8000 characters
            MarkdownParsing = true; // Default to enabling markdown parsing
            SearxNGInstance = ""; // Default to empty
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ApiKey = ApiKeyPasswordBox.Password;

            SaveIni(ConfigFileName);

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
            if (File.Exists(ConfigFileName))
            {
                var settings = LoadIni(ConfigFileName);

                LoadSettingValue(settings, "llmserver", value => ServerURL = value);
                LoadSettingValue(settings, "apikey", value => ApiKey = value);
                LoadSettingValue(settings, "model", value => Model = value);
                LoadSettingValue(settings, "sysprompt", value => SysPrompt = value.Trim('"'));
                LoadSettingValue(settings, "assistantname", value => AssistantName = value);
                LoadSettingValue(settings, "showtooloutput", value => ShowToolOutput = (value == "1"));
                LoadSettingValue(settings, "maxcontentlength", value =>
                {
                    if (int.TryParse(value, out int maxLength))
                        MaxContentLength = maxLength;
                });
                LoadSettingValue(settings, "markdownparsing", value => MarkdownParsing = (value == "1"));
                LoadSettingValue(settings, "searxnginstance", value => SearxNGInstance = value);

                LoadSettingValue(settings, "tools", ApplyToolSelection);
                LoadSettingValue(settings, "toolsrequiringapproval", ApplyToolsRequiringApprovalSelection);

                // Sync password box manually (not bound)
                ApiKeyPasswordBox.Password = ApiKey;
            }
            else
            {
                MessageBox.Show("INI file not found: " + ConfigFileName, "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadSettingValue(Dictionary<string, string> settings, string key, Action<string> setter)
        {
            if (settings.TryGetValue(key, out string value))
                setter(value);
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private List<string> GetSelectedToolsFromListBox(System.Windows.Controls.ListBox listBox)
        {
            var selectedTools = new List<string>();

            foreach (var item in listBox.SelectedItems)
            {
                var toolName = item as string;
                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    selectedTools.Add(toolName);
                }
            }

            return selectedTools;
        }

        private void SaveIni(string path)
        {
            var selectedTools = GetSelectedToolsFromListBox(ToolsListBox);
            var selectedToolsRequiringApproval = GetSelectedToolsFromListBox(ToolsRequiringApprovalListBox);

            var lines = new List<string>
            {
                "apikey=" + ApiKey,
                "assistantname=" + AssistantName,
                "llmserver=" + ServerURL,
                "maxcontentlength=" + MaxContentLength,
                "markdownparsing=" + (MarkdownParsing ? "1" : "0"),
                "model=" + Model,
                "searxnginstance=" + SearxNGInstance,
                "showtooloutput=" + (ShowToolOutput ? "1" : "0"),
                "sysprompt=\"" + SysPrompt + "\"", // keep quotes around prompt
                "tools=" + string.Join(",", selectedTools),
                "toolsrequiringapproval=" + string.Join(",", selectedToolsRequiringApproval)
            };

            File.WriteAllLines(path, lines);
        }

        private void ApplyToolSelectionToListBox(System.Windows.Controls.ListBox listBox, string toolsValue)
        {
            if (listBox == null || string.IsNullOrWhiteSpace(toolsValue))
                return;

            var requestedTools = toolsValue.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var requestedTool in requestedTools)
            {
                var trimmed = requestedTool.Trim();
                if (trimmed.Length == 0)
                    continue;

                foreach (var item in listBox.Items)
                {
                    var toolName = item as string;
                    if (toolName != null &&
                        string.Equals(toolName, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        listBox.SelectedItems.Add(item);
                        break;
                    }
                }
            }
        }

        private void ApplyToolSelection(string toolsValue)
        {
            ApplyToolSelectionToListBox(ToolsListBox, toolsValue);
        }

        private void ApplyToolsRequiringApprovalSelection(string toolsValue)
        {
            ApplyToolSelectionToListBox(ToolsRequiringApprovalListBox, toolsValue);
        }
    }
}
