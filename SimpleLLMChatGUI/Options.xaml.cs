using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SimpleLLMChatGUI
{
    public partial class Options : Window, INotifyPropertyChanged
    {
        private readonly ProcessHandler _processHandler;
        private readonly string _toolsDir;

        public event PropertyChangedEventHandler PropertyChanged;

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
            _availableTools = ToolOptionsRegistry.LoadToolDisplayNames(_toolsDir);
            _toolOptions = ToolOptionsRegistry.LoadOptionsFromDirectory(_toolsDir);

            _systemFonts = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .OrderBy(f => f)
                .ToList();
            _systemFonts.Insert(0, "Default");

            InitializeDefaults();
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

        private void ModelListButton_Click(object sender, RoutedEventArgs e)
        {
            string baseUrl = ServerURL != null ? ServerURL.Trim() : string.Empty;
            string picked = ModelChooserDialog.Pick(
                this,
                baseUrl,
                ApiKeyPasswordBox.Password,
                Model,
                "Enter an LLM server URL before listing models.");
            if (picked != null)
                Model = picked;
        }

        private void EmbeddingsModelListButton_Click(object sender, RoutedEventArgs e)
        {
            string baseUrl = EmbeddingsEndpoint;
            if (string.IsNullOrEmpty(baseUrl))
                baseUrl = ServerURL;
            if (baseUrl != null)
                baseUrl = baseUrl.Trim();

            string apiKey = EmbeddingsApiKeyPasswordBox.Password;
            if (string.IsNullOrEmpty(apiKey))
                apiKey = ApiKeyPasswordBox.Password;

            string picked = ModelChooserDialog.Pick(
                this,
                baseUrl,
                apiKey,
                EmbeddingsModel,
                "Enter an embeddings endpoint (or configure an LLM server) before listing models.");
            if (picked != null)
                EmbeddingsModel = picked;
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
            ThinkingDisplayModeText = config.GetChatBlockDisplayMode("thinkingdisplay", ChatBlockDisplayMode.Collapsed).ToString();
            ToolCallDisplayModeText = config.GetChatBlockDisplayMode("toolcalldisplay", ChatBlockDisplayMode.Collapsed).ToString();
            ToolOutputDisplayModeText = config.GetChatBlockDisplayMode("tooloutputdisplay", ChatBlockDisplayMode.Shown).ToString();
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
            EmbeddingsEndpoint = config.GetConfigValue("embeddingsEndpoint");
            EmbeddingsModel = config.GetConfigValue("embeddingsModel");
            EmbeddingsApiKey = config.GetConfigValue("embeddingsApiKey");

            ApiKeyPasswordBox.Password = ApiKey;
            EmbeddingsApiKeyPasswordBox.Password = EmbeddingsApiKey;
            ApplyRetrieveModeToCombo();
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
    }
}
