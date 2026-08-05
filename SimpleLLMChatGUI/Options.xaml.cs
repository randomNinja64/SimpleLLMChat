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
        private int _contextWindowSize;
        private string _assistantName;
        private bool _showToolOutput;
        private bool _showReasoningOutput;
        private bool _collapseThinking;
        private bool _collapseToolCalls;
        private bool _markdownParsing;
        private string _codeFontFamily;
        private string _customFontFamily;
        private int _chatFontSize;

        // RAG
        private bool _ragEnabled;
        private string _ragKnowledgePath;
        private int _ragMaxResults;
        private int _indexChunkLines;
        private int _indexChunkOverlap;
        private int _ragMaxSnippetLength;
        private string _ragRetrieveMode;
        private string _ragAllowedExtensions;
        private string _embeddingsEndpoint;
        private string _embeddingsModel;
        private string _embeddingsApiKey;
        private readonly ProcessHandler _processHandler;

        public event PropertyChangedEventHandler PropertyChanged;

        private readonly List<string> _availableTools;
        private readonly List<string> _systemFonts;

        // Dynamic tool options loaded from manifests
        private List<ToolOptionDefinition> _toolOptions;
        private readonly Dictionary<string, FrameworkElement> _toolOptionControls = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        private List<ScrollViewer> _toolGroupPages = new List<ScrollViewer>();

        // Per-tool timeout controls (keyed by tool name)
        private readonly Dictionary<string, TextBox> _toolTimeoutControls = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);

        private readonly string _toolsDir;

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

        public int ContextWindowSize
        {
            get { return _contextWindowSize; }
            set { _contextWindowSize = value; OnPropertyChanged(nameof(ContextWindowSize)); }
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

        public bool CollapseThinking
        {
            get { return _collapseThinking; }
            set { _collapseThinking = value; OnPropertyChanged(nameof(CollapseThinking)); }
        }

        public bool CollapseToolCalls
        {
            get { return _collapseToolCalls; }
            set { _collapseToolCalls = value; OnPropertyChanged(nameof(CollapseToolCalls)); }
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

        public bool RagEnabled
        {
            get { return _ragEnabled; }
            set { _ragEnabled = value; OnPropertyChanged(nameof(RagEnabled)); }
        }

        public string RagKnowledgePath
        {
            get { return _ragKnowledgePath; }
            set { _ragKnowledgePath = value; OnPropertyChanged(nameof(RagKnowledgePath)); }
        }

        public int RagMaxResults
        {
            get { return _ragMaxResults; }
            set { _ragMaxResults = value; OnPropertyChanged(nameof(RagMaxResults)); }
        }

        public int IndexChunkLines
        {
            get { return _indexChunkLines; }
            set { _indexChunkLines = value; OnPropertyChanged(nameof(IndexChunkLines)); }
        }

        public int IndexChunkOverlap
        {
            get { return _indexChunkOverlap; }
            set { _indexChunkOverlap = value; OnPropertyChanged(nameof(IndexChunkOverlap)); }
        }

        public int RagMaxSnippetLength
        {
            get { return _ragMaxSnippetLength; }
            set { _ragMaxSnippetLength = value; OnPropertyChanged(nameof(RagMaxSnippetLength)); }
        }

        public string RagRetrieveMode
        {
            get { return _ragRetrieveMode; }
            set { _ragRetrieveMode = value; OnPropertyChanged(nameof(RagRetrieveMode)); }
        }

        public string RagAllowedExtensions
        {
            get { return _ragAllowedExtensions; }
            set { _ragAllowedExtensions = value; OnPropertyChanged(nameof(RagAllowedExtensions)); }
        }

        public string EmbeddingsEndpoint
        {
            get { return _embeddingsEndpoint; }
            set { _embeddingsEndpoint = value; OnPropertyChanged(nameof(EmbeddingsEndpoint)); }
        }

        public string EmbeddingsModel
        {
            get { return _embeddingsModel; }
            set { _embeddingsModel = value; OnPropertyChanged(nameof(EmbeddingsModel)); }
        }

        public string EmbeddingsApiKey
        {
            get { return _embeddingsApiKey; }
            set { _embeddingsApiKey = value; OnPropertyChanged(nameof(EmbeddingsApiKey)); }
        }

        public Options()
            : this(null)
        {
        }

        public Options(ProcessHandler processHandler)
        {
            _processHandler = processHandler;
            InitializeComponent();
            DataContext = this;

            _toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");

            // Discover available tools from manifests in the tools/ directory
            _availableTools = LoadAvailableToolsFromManifests();

            // Discover tool options from manifests
            _toolOptions = ToolOptionsRegistry.LoadOptionsFromDirectory(_toolsDir);

            // Load system fonts
            _systemFonts = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .OrderBy(f => f)
                .ToList();
            _systemFonts.Insert(0, "Default");

            InitializeDefaults();
        }

        private void InitializeDefaults()
        {
            ServerURL = "";
            ApiKey = "";
            Model = "";
            SysPrompt = "";
            ContextWindowSize = 0;
            AssistantName = AppConstants.DefaultAssistantName;
            ShowToolOutput = true;
            ShowReasoningOutput = true;
            CollapseThinking = true;
            CollapseToolCalls = true;
            MarkdownParsing = true;
            CodeFontFamily = "";
            CustomFontFamily = "";
            ChatFontSize = AppConstants.DefaultChatFontSize;
            RagEnabled = false;
            RagKnowledgePath = "";
            RagMaxResults = 5;
            IndexChunkLines = 60;
            IndexChunkOverlap = 10;
            RagMaxSnippetLength = 2000;
            RagRetrieveMode = "newchat";
            RagAllowedExtensions = AppConstants.DefaultRagAllowedExtensions;
            EmbeddingsEndpoint = "";
            EmbeddingsModel = "";
            EmbeddingsApiKey = "";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentSettings();

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

        private void CategoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AppearancePage == null) return;

            AppearancePage.Visibility = Visibility.Collapsed;
            SystemPage.Visibility = Visibility.Collapsed;
            ToolsPage.Visibility = Visibility.Collapsed;
            if (RagPage != null)
                RagPage.Visibility = Visibility.Collapsed;
            foreach (var page in _toolGroupPages)
                page.Visibility = Visibility.Collapsed;

            switch (CategoryListBox.SelectedIndex)
            {
                case 0: AppearancePage.Visibility = Visibility.Visible; break;
                case 1:
                    if (RagPage != null)
                        RagPage.Visibility = Visibility.Visible;
                    break;
                case 2: SystemPage.Visibility = Visibility.Visible; break;
                case 3: ToolsPage.Visibility = Visibility.Visible; break;
                default:
                    int toolIdx = CategoryListBox.SelectedIndex - 4;
                    if (toolIdx >= 0 && toolIdx < _toolGroupPages.Count)
                        _toolGroupPages[toolIdx].Visibility = Visibility.Visible;
                    break;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FontHandler.ApplyFontToWindow(this);

            var config = App.Config;
            LoadSettings(config);
            LoadToolSelections(config);
            BuildToolOptionsUI(config);
            BuildToolTimeoutsUI(config);

            IndexingStatusHub.Updated += OnIndexingStatusUpdated;
            RefreshIndexingStatusUi();
            Closed += Options_Closed;
        }

        private void LoadSettings(ConfigHandler config)
        {
            ServerURL = config.GetConfigValue("llmserver");
            ApiKey = config.GetConfigValue("apikey");
            Model = config.GetConfigValue("model");
            SysPrompt = ConfigHandler.DecodeStoredPrompt(config.GetConfigValue("sysprompt"));
            ContextWindowSize = config.GetConfigInt("contextWindowSize", 0);
            AssistantName = config.GetConfigValue("assistantname");
            if (string.IsNullOrWhiteSpace(AssistantName))
                AssistantName = AppConstants.DefaultAssistantName;
            ShowToolOutput = config.GetConfigBool("showtooloutput");
            ShowReasoningOutput = config.GetConfigBool("showreasoningoutput");
            CollapseThinking = config.GetConfigBool("collapsethinking", true);
            CollapseToolCalls = config.GetConfigBool("collapsetoolcalls", true);
            MarkdownParsing = config.GetConfigBool("markdownparsing", true);
            CodeFontFamily = config.GetConfigValue("codeblockfontfamily");
            CustomFontFamily = config.GetConfigValue("customfontfamily");
            ChatFontSize = config.GetConfigInt("fontsize", AppConstants.DefaultChatFontSize);

            RagEnabled = config.GetConfigBool("ragEnabled", false);
            RagKnowledgePath = config.GetConfigValue("ragKnowledgePath");
            RagMaxResults = config.GetConfigInt("ragMaxResults", 5);
            IndexChunkLines = config.GetConfigInt("indexChunkLines", 60);
            IndexChunkOverlap = config.GetConfigInt("indexChunkOverlap", 10);
            RagMaxSnippetLength = config.GetConfigInt("ragMaxSnippetLength", 2000);
            RagRetrieveMode = config.GetConfigValue("ragRetrieveMode", "newchat");
            string allowedExt = config.GetConfigValue("ragAllowedExtensions");
            RagAllowedExtensions = string.IsNullOrEmpty(allowedExt)
                ? AppConstants.DefaultRagAllowedExtensions
                : allowedExt;
            EmbeddingsEndpoint = config.GetConfigString("embeddingsEndpoint") ?? string.Empty;
            EmbeddingsModel = config.GetConfigString("embeddingsModel") ?? string.Empty;
            EmbeddingsApiKey = config.GetConfigString("embeddingsApiKey") ?? string.Empty;

            ApiKeyPasswordBox.Password = ApiKey;
            EmbeddingsApiKeyPasswordBox.Password = EmbeddingsApiKey;
            ApplyRetrieveModeToCombo();
        }

        private void LoadToolSelections(ConfigHandler config)
        {
            ApplyToolSelectionToListBox(ToolsListBox, config.GetConfigList("tools"));
            ApplyToolSelectionToListBox(
                ToolsRequiringApprovalListBox,
                config.GetConfigList("toolsrequiringapproval"));
        }

        private void Options_Closed(object sender, EventArgs e)
        {
            IndexingStatusHub.Updated -= OnIndexingStatusUpdated;
        }

        private void OnIndexingStatusUpdated()
        {
            RefreshIndexingStatusUi();
        }

        private void RefreshIndexingStatusUi()
        {
            IndexingStatusSnapshot status = IndexingStatusHub.GetSnapshot();
            if (IndexingStatusBrief != null)
                IndexingStatusBrief.Text = status.BriefText ?? "Index: (unknown)";
            if (IndexingStatusDetail != null)
                IndexingStatusDetail.Text = status.DetailText ?? string.Empty;
            if (BuildIndexButton != null)
                BuildIndexButton.IsEnabled = !status.IsBusy;
            if (ClearIndexButton != null)
                ClearIndexButton.IsEnabled = !status.IsBusy;
        }

        private void ApplyRetrieveModeToCombo()
        {
            if (RagRetrieveModeComboBox == null)
                return;
            RagRetrieveModeComboBox.SelectedIndex =
                string.Equals(RagRetrieveMode, "everyturn", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        private void SyncRetrieveModeFromCombo()
        {
            if (RagRetrieveModeComboBox == null)
                return;
            RagRetrieveMode = RagRetrieveModeComboBox.SelectedIndex == 1 ? "everyturn" : "newchat";
        }

        private void BuildIndexButton_Click(object sender, RoutedEventArgs e)
        {
            // Persist current Options so the CLI picks them up on /reload,
            // which also runs the RAG index check.
            SaveCurrentSettings();
            App.LoadSettings();

            if (_processHandler != null && _processHandler.IsProcessRunning)
                _processHandler.SendReload();
            else
                MessageBox.Show(this, "Start a chat session first so the CLI can build the index.", "RAG", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveCurrentSettings()
        {
            ApiKey = ApiKeyPasswordBox.Password;
            EmbeddingsApiKey = EmbeddingsApiKeyPasswordBox.Password;
            SyncRetrieveModeFromCombo();
            SaveIni(App.ConfigFilePath);
        }

        private void ClearIndexButton_Click(object sender, RoutedEventArgs e)
        {
            if (_processHandler != null && _processHandler.IsProcessRunning)
                _processHandler.SendInput("/clearindex");
            else
                MessageBox.Show(this, "Start a chat session first so the CLI can clear the index.", "RAG", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private List<string> GetSelectedToolsFromListBox(System.Windows.Controls.ListBox listBox)
        {
            return listBox.SelectedItems
                .OfType<string>()
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        private void SaveIni(string path)
        {
            var sections = new List<KeyValuePair<string, List<string>>>
            {
                new KeyValuePair<string, List<string>>("Appearance", GetAppearanceSettings()),
                new KeyValuePair<string, List<string>>("RAG", GetRagSettings()),
                new KeyValuePair<string, List<string>>("System", GetSystemSettings()),
                new KeyValuePair<string, List<string>>("Tools", GetToolSettings()),
            };
            AddDynamicToolSettings(sections);
            SettingsIniWriter.WriteSections(path, sections);
        }

        private List<string> GetAppearanceSettings()
        {
            return new List<string>
            {
                "assistantname=" + AssistantName,
                "codeblockfontfamily=" + CodeFontFamily,
                "customfontfamily=" + CustomFontFamily,
                "fontsize=" + ChatFontSize,
                "markdownparsing=" + (MarkdownParsing ? "1" : "0"),
                "showreasoningoutput=" + (ShowReasoningOutput ? "1" : "0"),
                "collapsethinking=" + (CollapseThinking ? "1" : "0"),
                "showtooloutput=" + (ShowToolOutput ? "1" : "0"),
                "collapsetoolcalls=" + (CollapseToolCalls ? "1" : "0"),
            };
        }

        private List<string> GetSystemSettings()
        {
            return SettingsIniWriter.GetSystemSettings(ApiKey, ServerURL, Model, SysPrompt, ContextWindowSize);
        }

        private List<string> GetToolSettings()
        {
            var toolLines = new List<string>
            {
                "tools=" + string.Join(",", GetSelectedToolsFromListBox(ToolsListBox)),
                "toolsrequiringapproval=" + string.Join(",", GetSelectedToolsFromListBox(ToolsRequiringApprovalListBox)),
            };

            foreach (var kvp in _toolTimeoutControls)
            {
                string val = kvp.Value.Text.Trim();
                int parsed;
                if (!string.IsNullOrEmpty(val) && int.TryParse(val, out parsed) && parsed > 0)
                    toolLines.Add("tooltimeout." + kvp.Key.ToLowerInvariant() + "=" + parsed);
            }
            return toolLines;
        }

        private List<string> GetRagSettings()
        {
            string allowedExt = RagExtensionList.FormatForStorage(RagAllowedExtensions);
            return new List<string>
            {
                "ragenabled=" + (RagEnabled ? "1" : "0"),
                "ragallowedextensions=" + allowedExt,
                "indexchunkoverlap=" + IndexChunkOverlap,
                "indexchunklines=" + IndexChunkLines,
                "embeddingsapikey=" + (EmbeddingsApiKey ?? string.Empty),
                "embeddingsendpoint=" + (EmbeddingsEndpoint ?? string.Empty),
                "embeddingsmodel=" + (EmbeddingsModel ?? string.Empty),
                "ragknowledgepath=" + (RagKnowledgePath ?? string.Empty),
                "ragmaxsnippetlength=" + RagMaxSnippetLength,
                "ragmaxresults=" + RagMaxResults,
                "ragretrievemode=" + (RagRetrieveMode ?? "newchat"),
            };
        }

        private void AddDynamicToolSettings(List<KeyValuePair<string, List<string>>> sections)
        {
            foreach (var group in GetToolOptionGroups())
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
                sections.Add(new KeyValuePair<string, List<string>>(group.Key, groupLines));
            }
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

            foreach (var group in GetToolOptionGroups())
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
        private IOrderedEnumerable<IGrouping<string, ToolOptionDefinition>> GetToolOptionGroups()
        {
            return _toolOptions
                .GroupBy(opt => string.IsNullOrWhiteSpace(opt.Source) ? "Tools" : opt.Source, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        }

        private List<string> LoadAvailableToolsFromManifests()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toolNames = new List<string>();

            foreach (string jsonFile in ManifestScanner.GetManifestFiles(_toolsDir))
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
                        if (!string.IsNullOrEmpty(name) && seen.Add(name))
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

        private void ApplyToolSelectionToListBox(System.Windows.Controls.ListBox listBox, List<string> tools)
        {
            if (listBox == null || tools == null)
                return;

            var selectedTools = new HashSet<string>(tools, StringComparer.OrdinalIgnoreCase);
            foreach (var item in listBox.Items)
            {
                var toolName = item as string;
                if (toolName != null && selectedTools.Contains(toolName))
                    listBox.SelectedItems.Add(item);
            }
        }
    }
}
