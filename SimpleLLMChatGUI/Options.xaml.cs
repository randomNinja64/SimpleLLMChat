using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        private bool _markdownParsing;
        private string _codeFontFamily;
        private string _customFontFamily;
        private int _chatFontSize;
        private ProcessHandler _processHandler;

        public event PropertyChangedEventHandler PropertyChanged;

        private readonly List<string> _availableTools;
        private readonly List<string> _systemFonts;

        // Dynamic tool options loaded from manifests
        private List<ToolOptionDefinition> _toolOptions;
        private readonly Dictionary<string, FrameworkElement> _toolOptionControls = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);

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

        public bool MarkdownParsing
        {
            get { return _markdownParsing; }
            set { _markdownParsing = value; OnPropertyChanged(nameof(MarkdownParsing)); }
        }

        public string CodeFontFamily
        {
            get { return _codeFontFamily; }
            set { _codeFontFamily = value; OnPropertyChanged(nameof(CodeFontFamily)); }
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

            // Discover tool options from manifests
            string toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
            _toolOptions = ToolOptionsRegistry.LoadOptionsFromDirectory(toolsDir);

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
            ShowToolOutput = true;
            ShowReasoningOutput = true;
            MarkdownParsing = true;
            CodeFontFamily = "";
            CustomFontFamily = "";
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

        private static string EscapePromptForStorage(string prompt)
        {
            if (string.IsNullOrEmpty(prompt))
                return string.Empty;

            // Escape backslashes first, then other special characters
            return prompt.Replace("\\", "\\\\")
                        .Replace("\r\n", "\\n")  // Windows line endings
                        .Replace("\n", "\\n")      // Unix line endings
                        .Replace("\r", "\\r")      // Mac line endings
                        .Replace("\t", "\\t");      // Tabs
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
            ServerURL = config.GetConfigValue("llmserver");
            ApiKey = config.GetConfigValue("apikey");
            Model = config.GetConfigValue("model");
            SysPrompt = ConfigHandler.DecodeStoredPrompt(config.GetConfigValue("sysprompt"));
            AssistantName = config.GetConfigValue("assistantname");
            ShowToolOutput = config.GetConfigBool("showtooloutput");
            ShowReasoningOutput = config.GetConfigBool("showreasoningoutput");
            MarkdownParsing = config.GetConfigBool("markdownparsing", true);
            CodeFontFamily = config.GetConfigValue("codeblockfontfamily");
            CustomFontFamily = config.GetConfigValue("customfontfamily");
            ChatFontSize = config.GetConfigInt("fontsize", AppConstants.DefaultChatFontSize);

            var enabledTools = config.GetConfigList("tools");
            if (enabledTools.Count > 0)
                ApplyToolSelectionToListBox(ToolsListBox, string.Join(",", enabledTools));

            var approvalTools = config.GetConfigList("toolsrequiringapproval");
            if (approvalTools.Count > 0)
                ApplyToolSelectionToListBox(ToolsRequiringApprovalListBox, string.Join(",", approvalTools));

            // Sync password box manually (not bound)
            ApiKeyPasswordBox.Password = ApiKey;

            // Build dynamic tool options UI and populate values
            BuildToolOptionsUI(config);
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
                "codeblockfontfamily=" + CodeFontFamily,
                "customfontfamily=" + CustomFontFamily,
                "fontsize=" + ChatFontSize,
                "llmserver=" + ServerURL,
                "markdownparsing=" + (MarkdownParsing ? "1" : "0"),
                "model=" + Model,
                "showreasoningoutput=" + (ShowReasoningOutput ? "1" : "0"),
                "showtooloutput=" + (ShowToolOutput ? "1" : "0"),
                "sysprompt=\"" + EscapePromptForStorage(SysPrompt) + "\"", // keep quotes around prompt; escape sequences encoded
                "tools=" + string.Join(",", selectedTools),
                "toolsrequiringapproval=" + string.Join(",", selectedToolsRequiringApproval)
            };

            // Save dynamic tool options from manifests
            foreach (var opt in _toolOptions)
            {
                string value = opt.Default;
                FrameworkElement control;
                if (_toolOptionControls.TryGetValue(opt.Name, out control))
                {
                    if (opt.Type == "bool" && control is CheckBox cb)
                        value = cb.IsChecked == true ? "1" : "0";
                    else if (control is TextBox tb)
                        value = tb.Text;
                }
                lines.Add(opt.Name.ToLowerInvariant() + "=" + value);
            }

            lines.Sort(StringComparer.OrdinalIgnoreCase);
            File.WriteAllLines(path, lines);
        }

        /// <summary>
        /// Builds dynamic UI controls in ToolOptionsPanel from manifest-registered options,
        /// grouped by manifest source and sorted alphabetically by label within each group.
        /// </summary>
        private void BuildToolOptionsUI(ConfigHandler config)
        {
            ToolOptionsPanel.Children.Clear();
            _toolOptionControls.Clear();

            if (_toolOptions.Count == 0)
                return;

            var groupedOptions = _toolOptions
                .OrderBy(opt => opt.Source ?? "Tools", StringComparer.OrdinalIgnoreCase)
                .ThenBy(opt => opt.Type == "bool" ? 1 : 0)
                .ThenBy(opt => opt.Label, StringComparer.OrdinalIgnoreCase)
                .GroupBy(opt => string.IsNullOrWhiteSpace(opt.Source) ? "Tools" : opt.Source, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupedOptions)
            {
                var groupPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

                var heading = new Label
                {
                    Content = group.Key,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 0)
                };
                groupPanel.Children.Add(heading);

                var textOpts = group.Where(o => o.Type != "bool").ToList();
                var boolOpts = group.Where(o => o.Type == "bool").ToList();

                foreach (var opt in textOpts)
                {
                    string configValue = config.GetConfigString(opt.Name);
                    string currentValue = configValue ?? opt.Default;
                    var label = new Label { Content = opt.Label + ":" };
                    var textBox = new TextBox { Text = currentValue, Height = 23 };
                    groupPanel.Children.Add(label);
                    groupPanel.Children.Add(textBox);
                    _toolOptionControls[opt.Name] = textBox;
                }

                if (boolOpts.Count > 0)
                {
                    // Spacer between textbox group and checkbox group
                    if (textOpts.Count > 0)
                        groupPanel.Children.Add(new Border { Height = 8 });

                    foreach (var opt in boolOpts)
                    {
                        string configValue = config.GetConfigString(opt.Name);
                        string currentValue = configValue ?? opt.Default;
                        var checkBox = new CheckBox
                        {
                            Content = opt.Label,
                            Margin = new Thickness(0, 4, 0, 0)
                        };
                        checkBox.IsChecked = currentValue == "1" || string.Equals(currentValue, "true", StringComparison.OrdinalIgnoreCase);
                        groupPanel.Children.Add(checkBox);
                        _toolOptionControls[opt.Name] = checkBox;
                    }
                }

                ToolOptionsPanel.Children.Add(groupPanel);
            }
        }

        /// <summary>
        /// Scans the tools/ directory (and one level of subdirectories) for *.json manifests
        /// and returns a sorted list of all discovered tool names.
        /// </summary>
        private List<string> LoadAvailableToolsFromManifests()
        {
            var toolNames = new List<string>();
            string toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");

            foreach (string jsonFile in ManifestScanner.GetManifestFiles(toolsDir))
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
