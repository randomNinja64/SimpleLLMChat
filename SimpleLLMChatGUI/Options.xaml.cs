using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

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
        private bool _showReasoningOutput;
        private int _maxContentLength;
        private int _maxSearchResults;
        private bool _markdownParsing;
        private string _searxngInstance;
        private string _customFontFamily;
        private int _chatFontSize;
        private ProcessHandler _processHandler;

        public event PropertyChangedEventHandler PropertyChanged;

        private readonly List<string> _availableTools;
        private readonly List<string> _systemFonts;

        public List<string> AvailableTools
        {
            get { return _availableTools; }
        }

        public List<string> SystemFonts
        {
            get { return _systemFonts; }
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

        public bool ShowReasoningOutput
        {
            get { return _showReasoningOutput; }
            set { _showReasoningOutput = value; OnPropertyChanged(nameof(ShowReasoningOutput)); }
        }

        public int MaxContentLength
        {
            get { return _maxContentLength; }
            set { _maxContentLength = value; OnPropertyChanged(nameof(MaxContentLength)); }
        }

        public int MaxSearchResults
        {
            get { return _maxSearchResults; }
            set { _maxSearchResults = value; OnPropertyChanged(nameof(MaxSearchResults)); }
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

        public string CustomFontFamily
        {
            get { return _customFontFamily; }
            set { _customFontFamily = value; OnPropertyChanged(nameof(CustomFontFamily)); }
        }

        public int ChatFontSize
        {
            get { return _chatFontSize; }
            set { _chatFontSize = value; OnPropertyChanged(nameof(ChatFontSize)); }
        }

        public Options(ProcessHandler processHandler)
        {
            InitializeComponent();
            DataContext = this;

            _processHandler = processHandler;

            // Discover available tools from manifests in the tools/ directory
            _availableTools = LoadAvailableToolsFromManifests();

            // Load system fonts
            _systemFonts = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .OrderBy(f => f)
                .ToList();
            _systemFonts.Insert(0, "Default");

            // Default values
            ServerURL = "";
            ApiKey = "";
            Model = "";
            SysPrompt = "";
            AssistantName = "";
            ShowToolOutput = true; // Default to showing tool outputs
            ShowReasoningOutput = true; // Default to showing reasoning output
            MaxContentLength = AppConstants.DefaultMaxContentLength;
            MaxSearchResults = AppConstants.DefaultMaxSearchResults;
            MarkdownParsing = true; // Default to enabling markdown parsing
            SearxNGInstance = ""; // Default to empty
            CustomFontFamily = ""; // Default to empty (use system default)
            ChatFontSize = AppConstants.DefaultChatFontSize;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ApiKey = ApiKeyPasswordBox.Password;

            SaveIni(App.ConfigFileName);

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

        private void ChooseColorsButton_Click(object sender, RoutedEventArgs e)
        {
            var colorsForm = new ColorsForm
            {
                Owner = this
            };
            colorsForm.ShowDialog();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Apply custom font to this window
            FontHandler.ApplyFontToWindow(this);

            var config = App.Config;
            ServerURL = config.GetLLMEndpoint();
            ApiKey = config.GetApiKey();
            Model = config.GetModel();
            SysPrompt = config.GetSysPrompt().Trim('"');
            AssistantName = config.GetAssistantName();
            ShowToolOutput = config.GetShowToolOutput();
            ShowReasoningOutput = config.GetShowReasoningOutput();
            MaxContentLength = config.GetMaxContentLength();
            MaxSearchResults = config.GetMaxSearchResults();
            MarkdownParsing = config.GetMarkdownParsing();
            SearxNGInstance = config.GetSearxNGInstance();
            CustomFontFamily = config.GetCustomFontFamily();
            ChatFontSize = config.GetFontSize();

            var enabledTools = config.GetEnabledTools();
            if (enabledTools.Count > 0)
                ApplyToolSelectionToListBox(ToolsListBox, string.Join(",", enabledTools));

            var approvalTools = config.GetToolsRequiringApproval();
            if (approvalTools.Count > 0)
                ApplyToolSelectionToListBox(ToolsRequiringApprovalListBox, string.Join(",", approvalTools));

            // Sync password box manually (not bound)
            ApiKeyPasswordBox.Password = ApiKey;
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
                "customfontfamily=" + CustomFontFamily,
                "fontsize=" + ChatFontSize,
                "llmserver=" + ServerURL,
                "maxcontentlength=" + MaxContentLength,
                "maxsearchresults=" + MaxSearchResults,
                "markdownparsing=" + (MarkdownParsing ? "1" : "0"),
                "model=" + Model,
                "searxnginstance=" + SearxNGInstance,
                "showreasoningoutput=" + (ShowReasoningOutput ? "1" : "0"),
                "showtooloutput=" + (ShowToolOutput ? "1" : "0"),
                "sysprompt=\"" + SysPrompt + "\"", // keep quotes around prompt
                "tools=" + string.Join(",", selectedTools),
                "toolsrequiringapproval=" + string.Join(",", selectedToolsRequiringApproval)
            };

            File.WriteAllLines(path, lines);
        }

        /// <summary>
        /// Scans the tools/ directory (and one level of subdirectories) for *.json manifests
        /// and returns a sorted list of all discovered tool names.
        /// </summary>
        private List<string> LoadAvailableToolsFromManifests()
        {
            var toolNames = new List<string>();
            string toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");

            if (!Directory.Exists(toolsDir))
                return toolNames;

            var jsonFiles = new List<string>();
            jsonFiles.AddRange(Directory.GetFiles(toolsDir, "*.json"));
            foreach (string subDir in Directory.GetDirectories(toolsDir))
                jsonFiles.AddRange(Directory.GetFiles(subDir, "*.json"));

            foreach (string jsonFile in jsonFiles)
            {
                try
                {
                    string json = File.ReadAllText(jsonFile, System.Text.Encoding.UTF8);
                    JObject manifest = JObject.Parse(json);
                    JArray tools = manifest["tools"] as JArray;
                    if (tools == null) continue;

                    foreach (JObject tool in tools)
                    {
                        string name = (string)tool["name"];
                        if (!string.IsNullOrEmpty(name) && !toolNames.Contains(name))
                            toolNames.Add(name);
                    }
                }
                catch
                {
                    // Skip malformed manifests
                }
            }

            toolNames.Sort();
            return toolNames;
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
    }
}
