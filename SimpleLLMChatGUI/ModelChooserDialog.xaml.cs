using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// Dialog that fetches models from an OpenAI-compatible <c>/v1/models</c> endpoint
    /// and lets the user pick one.
    /// </summary>
    public partial class ModelChooserDialog : Window
    {
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _currentModel;
        private bool _loaded;

        public string SelectedModel { get; private set; }

        public ModelChooserDialog(string baseUrl, string apiKey, string currentModel)
        {
            _baseUrl = baseUrl ?? string.Empty;
            _apiKey = apiKey ?? string.Empty;
            _currentModel = currentModel ?? string.Empty;
            SelectedModel = string.Empty;
            InitializeComponent();
            FontHandler.ApplyFontToWindow(this);
        }

        /// <summary>
        /// Shows the chooser. Returns the selected model id, or <c>null</c> if cancelled
        /// or the base URL was empty (a warning is shown in that case).
        /// </summary>
        public static string Pick(Window owner, string baseUrl, string apiKey, string currentModel, string emptyUrlMessage)
        {
            if (string.IsNullOrEmpty(baseUrl))
            {
                MessageBox.Show(
                    owner,
                    emptyUrlMessage,
                    "Model List",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            var dialog = new ModelChooserDialog(baseUrl, apiKey, currentModel)
            {
                Owner = owner
            };
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedModel))
                return dialog.SelectedModel;
            return null;
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            if (_loaded)
                return;
            _loaded = true;
            LoadModels();
        }

        private void LoadModels()
        {
            Cursor previous = Cursor;
            Cursor = Cursors.Wait;
            try
            {
                IList<string> models = ModelsClient.ListModels(_baseUrl, _apiKey);
                ModelsListBox.Items.Clear();
                foreach (string id in models)
                    ModelsListBox.Items.Add(id);

                int selected = -1;
                if (!string.IsNullOrEmpty(_currentModel))
                {
                    for (int i = 0; i < ModelsListBox.Items.Count; i++)
                    {
                        if (string.Equals(
                            ModelsListBox.Items[i] as string,
                            _currentModel,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            selected = i;
                            break;
                        }
                    }
                }
                if (selected < 0 && ModelsListBox.Items.Count > 0)
                    selected = 0;
                if (selected >= 0)
                    ModelsListBox.SelectedIndex = selected;

                StatusText.Text = models.Count + " model" + (models.Count == 1 ? "" : "s") + " found";
                OkButton.IsEnabled = ModelsListBox.Items.Count > 0;
                ModelsListBox.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Model List",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                DialogResult = false;
            }
            finally
            {
                Cursor = previous;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (ModelsListBox.SelectedItem == null)
            {
                MessageBox.Show(
                    this,
                    "Select a model from the list.",
                    "Model List",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            SelectedModel = ModelsListBox.SelectedItem.ToString();
            DialogResult = true;
        }

        private void ModelsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ModelsListBox.SelectedItem != null)
                OkButton_Click(sender, e);
        }
    }
}
