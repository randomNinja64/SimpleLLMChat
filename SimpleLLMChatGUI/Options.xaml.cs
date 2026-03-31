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
        private List<ScrollViewer> _toolGroupPages = new List<ScrollViewer>();

        // Per-tool timeout controls (keyed by tool name)
        private readonly Dictionary<string, TextBox> _toolTimeoutControls = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);

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

        private void CategoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AppearancePage == null) return;

            AppearancePage.Visibility = Visibility.Collapsed;
            SystemPage.Visibility = Visibility.Collapsed;
            ToolsPage.Visibility = Visibility.Collapsed;
            foreach (var page in _toolGroupPages)
                page.Visibility = Visibility.Collapsed;

            switch (CategoryListBox.SelectedIndex)
            {
                case 0: AppearancePage.Visibility = Visibility.Visible; break;
                case 1: SystemPage.Visibility = Visibility.Visible; break;
                case 2: ToolsPage.Visibility = Visibility.Visible; break;
                default:
                    int toolIdx = CategoryListBox.SelectedIndex - 3;
                    if (toolIdx >= 0 && toolIdx < _toolGroupPages.Count)
                        _toolGroupPages[toolIdx].Visibility = Visibility.Visible;
                    break;
            }
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

            // Build per-tool timeout UI
            BuildToolTimeoutsUI(config);
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

            var allLines = new List<string>();

            // [Appearance]
            var appearanceLines = new List<string>
            {
                "assistantname=" + AssistantName,
                "codeblockfontfamily=" + CodeFontFamily,
                "customfontfamily=" + CustomFontFamily,
                "fontsize=" + ChatFontSize,
                "markdownparsing=" + (MarkdownParsing ? "1" : "0"),
                "showreasoningoutput=" + (ShowReasoningOutput ? "1" : "0"),
                "showtooloutput=" + (ShowToolOutput ? "1" : "0"),
            };
            appearanceLines.Sort(StringComparer.OrdinalIgnoreCase);
            allLines.Add("[Appearance]");
            allLines.AddRange(appearanceLines);

            // [System]
            var systemLines = new List<string>
            {
                "apikey=" + ApiKey,
                "llmserver=" + ServerURL,
                "model=" + Model,
                "sysprompt=\"" + EscapePromptForStorage(SysPrompt) + "\"", // keep quotes around prompt; escape sequences encoded
            };
            systemLines.Sort(StringComparer.OrdinalIgnoreCase);
            allLines.Add(string.Empty);
            allLines.Add("[System]");
            allLines.AddRange(systemLines);

            // [Tools]
            var toolLines = new List<string>
            {
                "tools=" + string.Join(",", selectedTools),
                "toolsrequiringapproval=" + string.Join(",", selectedToolsRequiringApproval),
            };
            foreach (var kvp in _toolTimeoutControls)
            {
                string val = kvp.Value.Text.Trim();
                int parsed;
                if (!string.IsNullOrEmpty(val) && int.TryParse(val, out parsed) && parsed > 0)
                    toolLines.Add("tooltimeout." + kvp.Key.ToLowerInvariant() + "=" + parsed);
            }
            toolLines.Sort(StringComparer.OrdinalIgnoreCase);
            allLines.Add(string.Empty);
            allLines.Add("[Tools]");
            allLines.AddRange(toolLines);

            // Dynamic tool option groups from manifests, one section per tool group
            if (_toolOptions.Count > 0)
            {
                var groupedOptions = _toolOptions
                    .GroupBy(opt => string.IsNullOrWhiteSpace(opt.Source) ? "Tools" : opt.Source, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

                foreach (var group in groupedOptions)
                {
                    var groupLines = new List<string>();
                    foreach (var opt in group)
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
                        groupLines.Add(opt.Name.ToLowerInvariant() + "=" + value);
                    }
                    groupLines.Sort(StringComparer.OrdinalIgnoreCase);
                    allLines.Add(string.Empty);
                    allLines.Add("[" + group.Key + "]");
                    allLines.AddRange(groupLines);
                }
            }

            File.WriteAllLines(path, allLines);
        }

        /// <summary>
        /// Builds per-tool-group pages: one ListBoxItem and one ScrollViewer page per group,
        /// added dynamically after the static Appearance/System/Tools entries.
        /// </summary>
        private void BuildToolOptionsUI(ConfigHandler config)
        {
            _toolOptionControls.Clear();
            _toolGroupPages.Clear();

            if (_toolOptions.Count == 0)
                return;

            var groupedOptions = _toolOptions
                .GroupBy(opt => string.IsNullOrWhiteSpace(opt.Source) ? "Tools" : opt.Source, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupedOptions)
            {
                // Add category item to ListBox
                var listBoxItem = new ListBoxItem
                {
                    Content = group.Key
                };
                CategoryListBox.Items.Add(listBoxItem);

                // Build page
                var stackPanel = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };

                var heading = new Label
                {
                    Content = group.Key,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14
                };
                stackPanel.Children.Add(heading);

                var textOpts = group.Where(o => o.Type != "bool").OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList();
                var boolOpts = group.Where(o => o.Type == "bool").OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList();

                foreach (var opt in textOpts)
                {
                    string configValue = config.GetConfigString(opt.Name);
                    string currentValue = configValue ?? opt.Default;
                    var label = new Label { Content = opt.Label + ":" };
                    var textBox = new TextBox { Text = currentValue, Height = 23 };
                    stackPanel.Children.Add(label);
                    stackPanel.Children.Add(textBox);
                    _toolOptionControls[opt.Name] = textBox;
                }

                if (boolOpts.Count > 0)
                {
                    if (textOpts.Count > 0)
                        stackPanel.Children.Add(new Border { Height = 8 });

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
                        stackPanel.Children.Add(checkBox);
                        _toolOptionControls[opt.Name] = checkBox;
                    }
                }

                var scrollViewer = new ScrollViewer
                {
                    Margin = new Thickness(0, 0, 0, 10),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Visibility = Visibility.Collapsed,
                    Content = stackPanel
                };

                ContentGrid.Children.Add(scrollViewer);
                _toolGroupPages.Add(scrollViewer);
            }
        }

        /// <summary>
        /// Adds a per-tool timeout section to the Tools page.
        /// Each known tool gets a label and a small TextBox for its timeout in seconds.
        /// Values are stored in config as tooltimeout.&lt;toolname&gt;.
        /// </summary>
        private void BuildToolTimeoutsUI(ConfigHandler config)
        {
            _toolTimeoutControls.Clear();

            if (_availableTools.Count == 0)
                return;

            var stack = ToolsPage.Content as StackPanel;
            if (stack == null)
                return;

            stack.Children.Add(new Label
            {
                Content = "Tool Timeouts (seconds, 0 or blank = no timeout):",
                Margin = new Thickness(0, 8, 0, 0)
            });

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            foreach (string toolName in _availableTools)
            {
                int rowIdx = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = toolName,
                    Padding = new Thickness(0, 2, 4, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "LabelTextColorBrush");
                Grid.SetRow(label, rowIdx);
                Grid.SetColumn(label, 0);

                string configVal = config.GetConfigString("tooltimeout." + toolName.ToLowerInvariant()) ?? "0";
                var textBox = new TextBox
                {
                    Text = configVal,
                    Height = 23,
                    Margin = new Thickness(0, 2, 0, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                textBox.PreviewTextInput += (s, e) => { e.Handled = !e.Text.All(char.IsDigit); };
                DataObject.AddPastingHandler(textBox, (s, e) =>
                {
                    if (e.DataObject.GetDataPresent(DataFormats.Text))
                    {
                        string text = (string)e.DataObject.GetData(DataFormats.Text);
                        if (!text.All(char.IsDigit)) e.CancelCommand();
                    }
                    else e.CancelCommand();
                });
                Grid.SetRow(textBox, rowIdx);
                Grid.SetColumn(textBox, 1);

                grid.Children.Add(label);
                grid.Children.Add(textBox);
                _toolTimeoutControls[toolName] = textBox;
            }

            stack.Children.Add(grid);
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
