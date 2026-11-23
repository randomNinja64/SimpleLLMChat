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
        private readonly bool _setupMode;

        public event PropertyChangedEventHandler PropertyChanged;

        public Options()
            : this(null)
        {
        }

        public Options(ProcessHandler processHandler)
            : this(processHandler, false)
        {
        }

        /// <param name="setupMode">
        /// First-run setup: pages are stepped through with Back/Next, System comes first,
        /// and tool pages are left out when no tools are installed.
        /// </param>
        private Options(ProcessHandler processHandler, bool setupMode)
        {
            _processHandler = processHandler;
            _setupMode = setupMode;
            InitializeComponent();
            DataContext = this;

            // Setup hides the category list, so it does not need the extra width.
            // Set before the window is shown so it is centered at this size.
            if (setupMode)
            {
                MinWidth = 620;
                MaxWidth = 660;
                Width = 620;
            }

            AppearanceItem.Tag = AppearancePage;
            RagItem.Tag = RagPage;
            SystemItem.Tag = SystemPage;
            ToolsItem.Tag = ToolsPage;

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

        private void JevModelListButton_Click(object sender, RoutedEventArgs e)
        {
            string baseUrl = JevModelsBaseUrl(JevBaseUrl);
            string picked = ModelChooserDialog.Pick(
                this,
                baseUrl,
                JevApiKeyPasswordBox.Password,
                JevModel,
                "Enter a Jev-compatible URL before listing models.");
            if (picked != null)
                JevModel = picked;
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
            // Fires during InitializeComponent, before the pages exist.
            if (ContentGrid == null) return;

            foreach (UIElement page in ContentGrid.Children)
                page.Visibility = Visibility.Collapsed;

            var item = CategoryListBox.SelectedItem as ListBoxItem;
            var selectedPage = item != null ? item.Tag as UIElement : null;
            if (selectedPage != null)
                selectedPage.Visibility = Visibility.Visible;

            if (_setupMode)
                UpdateSetupButtons();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FontHandler.ApplyFontToWindow(this);

            var config = App.Config;
            LoadSettings(config);
            BuildToolOptionsUI(config);
            BuildToolModesUI(config);
            RestrictToDigits(MaxConsecutiveFailedToolCallsTextBox);

            if (_setupMode)
                EnterSetupMode();

            IndexingStatusHub.Updated += OnIndexingStatusUpdated;
            RefreshIndexingStatusUi();
            Closed += Options_Closed;
        }

        /// <summary>
        /// Shows Options as the first-run setup wizard when LLMSettings.ini does not exist.
        /// Returns true when the user finished setup and the settings were written.
        /// </summary>
        public static bool ShowSetupIfNeeded(Window owner)
        {
            if (!App.NeedsOnboarding)
                return false;

            var setup = new Options(null, true);
            if (owner != null)
                setup.Owner = owner;
            return setup.ShowDialog() == true;
        }

        private void EnterSetupMode()
        {
            Title = "SimpleLLMChat Setup";
            SetupHeader.Visibility = Visibility.Visible;

            // Step-by-step: the category list stays as the step model but is not shown,
            // and pages take the full width.
            CategoryListBox.Visibility = Visibility.Collapsed;
            Grid.SetColumn(ContentGrid, 0);
            Grid.SetColumnSpan(ContentGrid, 2);

            // The header shows the step title, so drop each page's own heading.
            foreach (object page in ContentGrid.Children)
            {
                Label heading = FindPageHeading(page as DependencyObject);
                if (heading != null)
                    heading.Visibility = Visibility.Collapsed;
            }

            // The connection settings are the only ones needed to chat, so they come first.
            CategoryListBox.Items.Remove(SystemItem);
            CategoryListBox.Items.Insert(0, SystemItem);

            // Nothing in tools\ means nothing to enable, approve, or time out.
            if (_availableTools.Count == 0)
                CategoryListBox.Items.Remove(ToolsItem);

            // Indexing needs a running CLI, which setup does not have yet.
            RagIndexPanel.Visibility = Visibility.Collapsed;

            // Back / Next / Cancel; Next becomes Finish on the last step.
            BackButton.Visibility = Visibility.Visible;
            NextButton.Visibility = Visibility.Visible;
            NextButton.IsDefault = true;
            SaveButton.Visibility = Visibility.Collapsed;
            SaveButton.IsDefault = false;

            CategoryListBox.SelectedIndex = 0;
            UpdateSetupButtons();
        }

        /// <summary>A page's bold section heading: the first size-14 Label in it.</summary>
        private static Label FindPageHeading(DependencyObject node)
        {
            if (node == null)
                return null;
            var label = node as Label;
            if (label != null && label.FontSize == 14)
                return label;
            foreach (object child in LogicalTreeHelper.GetChildren(node))
            {
                Label found = FindPageHeading(child as DependencyObject);
                if (found != null)
                    return found;
            }
            return null;
        }

        private bool IsLastSetupStep
        {
            get { return CategoryListBox.SelectedIndex >= CategoryListBox.Items.Count - 1; }
        }

        private void UpdateSetupButtons()
        {
            int index = CategoryListBox.SelectedIndex;
            var item = CategoryListBox.SelectedItem as ListBoxItem;
            SetupStepTitle.Text = string.Format("{0} ({1} of {2})",
                item != null ? item.Content : string.Empty, index + 1, CategoryListBox.Items.Count);
            BackButton.IsEnabled = index > 0;
            NextButton.Content = IsLastSetupStep ? "Finish" : "Next >";
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (CategoryListBox.SelectedIndex > 0)
                CategoryListBox.SelectedIndex--;
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsLastSetupStep)
                SaveButton_Click(sender, e);
            else
                CategoryListBox.SelectedIndex++;
        }

        private void LoadSettings(ConfigHandler config)
        {
            ServerURL = config.Get(AppSettings.LlmServer);
            ApiKey = config.Get(AppSettings.ApiKey);
            Model = config.Get(AppSettings.Model);
            SysPrompt = ConfigHandler.DecodeStoredPrompt(config.Get(AppSettings.SysPrompt));
            ContextWindowSize = config.Get(AppSettings.ContextWindowSize);
            AssistantName = config.Get(AppSettings.AssistantName);
            if (string.IsNullOrWhiteSpace(AssistantName))
                AssistantName = AppConstants.DefaultAssistantName;
            ThinkingDisplayModeText = config.Get(AppSettings.ThinkingDisplay).ToString();
            ToolCallDisplayModeText = config.Get(AppSettings.ToolCallDisplay).ToString();
            ToolOutputDisplayModeText = config.Get(AppSettings.ToolOutputDisplay).ToString();
            MarkdownParsing = config.Get(AppSettings.MarkdownParsing);
            CodeFontFamily = config.Get(AppSettings.CodeBlockFontFamily);
            CustomFontFamily = config.Get(AppSettings.CustomFontFamily);
            ChatFontSize = config.Get(AppSettings.FontSize);

            RagEnabled = config.Get(AppSettings.RagEnabled);
            RagKnowledgePath = config.Get(AppSettings.RagKnowledgePath);
            RagMaxResults = config.Get(AppSettings.RagMaxResults);
            IndexChunkLines = config.Get(AppSettings.IndexChunkLines);
            IndexChunkOverlap = config.Get(AppSettings.IndexChunkOverlap);
            RagMaxSnippetLength = config.Get(AppSettings.RagMaxSnippetLength);
            RagRetrieveMode = config.Get(AppSettings.RagRetrieveMode);
            string allowedExt = config.Get(AppSettings.RagAllowedExtensions);
            RagAllowedExtensions = string.IsNullOrEmpty(allowedExt)
                ? AppConstants.DefaultRagAllowedExtensions
                : allowedExt;
            EmbeddingsEndpoint = config.Get(AppSettings.EmbeddingsEndpoint);
            EmbeddingsModel = config.Get(AppSettings.EmbeddingsModel);
            EmbeddingsApiKey = config.Get(AppSettings.EmbeddingsApiKey);
            JevBaseUrl = config.Get(AppSettings.JevBaseUrl);
            JevApiKey = config.Get(AppSettings.JevApiKey);
            JevModel = config.Get(AppSettings.JevModel);
            ShowJevConfidence = config.Get(AppSettings.ShowJevConfidence);
            MaxConsecutiveFailedToolCalls = config.Get(AppSettings.MaxConsecutiveFailedToolCalls);

            ApiKeyPasswordBox.Password = ApiKey;
            EmbeddingsApiKeyPasswordBox.Password = EmbeddingsApiKey;
            JevApiKeyPasswordBox.Password = JevApiKey;
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
            JevApiKey = JevApiKeyPasswordBox.Password;
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

        private static string JevModelsBaseUrl(string decisionUrl)
        {
            string url = decisionUrl != null ? decisionUrl.Trim().TrimEnd('/') : string.Empty;
            const string suffix = "/systemone";
            if (url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                url = url.Substring(0, url.Length - suffix.Length).TrimEnd('/');
            return url;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
