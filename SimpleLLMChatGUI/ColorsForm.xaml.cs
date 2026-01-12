using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Forms;
using System.Drawing;

namespace SimpleLLMChatGUI
{
    public partial class ColorsForm : Window
    {
        private class ColorSetting
        {
            public ColorConfig Config { get; set; }
            public System.Windows.Media.Color? Value { get; set; }
            public Border PreviewBorder { get; set; }
        }

        private Dictionary<string, ColorSetting> _colorSettings;

        public System.Windows.Media.Color? ButtonTextColor
        {
            get { return _colorSettings["buttontextcolor"].Value; }
            set { SetColor("buttontextcolor", value); }
        }

        public System.Windows.Media.Color? LabelTextColor
        {
            get { return _colorSettings["labeltextcolor"].Value; }
            set { SetColor("labeltextcolor", value); }
        }

        public System.Windows.Media.Color? ChatBackgroundColor
        {
            get { return _colorSettings["chatbackgroundcolor"].Value; }
            set { SetColor("chatbackgroundcolor", value); }
        }

        public System.Windows.Media.Color? ChatTextColor
        {
            get { return _colorSettings["chattextcolor"].Value; }
            set { SetColor("chattextcolor", value); }
        }

        public System.Windows.Media.Color? CodeBlockBackgroundColor
        {
            get { return _colorSettings["codeblockbackgroundcolor"].Value; }
            set { SetColor("codeblockbackgroundcolor", value); }
        }

        public System.Windows.Media.Color? WindowBackgroundColor
        {
            get { return _colorSettings["windowbackgroundcolor"].Value; }
            set { SetColor("windowbackgroundcolor", value); }
        }

        public ColorsForm()
        {
            InitializeComponent();
            InitializeColorSettings();
        }

        private void InitializeColorSettings()
        {
            _colorSettings = new Dictionary<string, ColorSetting>();
            
            // Map preview borders to keys
            var previewBorders = new Dictionary<string, Border>
            {
                { "buttontextcolor", ButtonTextColorPreview },
                { "chatbackgroundcolor", ChatBackgroundColorPreview },
                { "chattextcolor", ChatTextColorPreview },
                { "codeblockbackgroundcolor", CodeBlockBackgroundColorPreview },
                { "labeltextcolor", LabelTextColorPreview },
                { "windowbackgroundcolor", WindowBackgroundColorPreview }
            };

            // Use centralized color configuration
            foreach (var config in ColorHelper.ColorConfigs.Values)
            {
                if (previewBorders.TryGetValue(config.Key, out Border previewBorder))
                {
                    _colorSettings[config.Key] = new ColorSetting
                    {
                        Config = config,
                        PreviewBorder = previewBorder
                    };
                }
            }
        }

        private void SetColor(string key, System.Windows.Media.Color? value)
        {
            if (_colorSettings.ContainsKey(key))
            {
                _colorSettings[key].Value = value;
                UpdatePreview(key);
            }
        }

        private void UpdatePreview(string key)
        {
            if (_colorSettings.ContainsKey(key))
            {
                var setting = _colorSettings[key];
                var displayColor = setting.Value ?? setting.Config.DefaultSystemColor;
                setting.PreviewBorder.Background = new SolidColorBrush(displayColor);
            }
        }


        private void ShowColorDialog(System.Windows.Media.Color? currentColor, System.Windows.Media.Color defaultColor, Action<System.Windows.Media.Color> onColorSelected)
        {
            using (var colorDialog = new ColorDialog())
            {
                // Use current color if set, otherwise use system color for dialog
                System.Windows.Media.Color dialogColor = currentColor ?? defaultColor;
                colorDialog.Color = System.Drawing.Color.FromArgb(
                    dialogColor.A,
                    dialogColor.R,
                    dialogColor.G,
                    dialogColor.B);

                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // Convert System.Drawing.Color back to WPF Color
                    var selectedColor = colorDialog.Color;
                    onColorSelected(System.Windows.Media.Color.FromArgb(
                        selectedColor.A,
                        selectedColor.R,
                        selectedColor.G,
                        selectedColor.B));
                }
            }
        }

        private void ButtonTextColorButton_Click(object sender, RoutedEventArgs e)
        {
            ShowColorDialogForSetting("buttontextcolor", color => ButtonTextColor = color);
        }

        private void ClearButtonTextColorButton_Click(object sender, RoutedEventArgs e)
        {
            ButtonTextColor = null;
        }

        private void ChatBackgroundColorButton_Click(object sender, RoutedEventArgs e)
        {
            ShowColorDialogForSetting("chatbackgroundcolor", color => ChatBackgroundColor = color);
        }

        private void ClearChatBackgroundColorButton_Click(object sender, RoutedEventArgs e)
        {
            ChatBackgroundColor = null;
        }

        private void ChatTextColorButton_Click(object sender, RoutedEventArgs e)
        {
            ShowColorDialogForSetting("chattextcolor", color => ChatTextColor = color);
        }

        private void ClearChatTextColorButton_Click(object sender, RoutedEventArgs e)
        {
            ChatTextColor = null;
        }

        private void CodeBlockBackgroundColorButton_Click(object sender, RoutedEventArgs e)
        {
            ShowColorDialogForSetting("codeblockbackgroundcolor", color => CodeBlockBackgroundColor = color);
        }

        private void ClearCodeBlockBackgroundColorButton_Click(object sender, RoutedEventArgs e)
        {
            CodeBlockBackgroundColor = null;
        }

        private void LabelTextColorButton_Click(object sender, RoutedEventArgs e)
        {
            ShowColorDialogForSetting("labeltextcolor", color => LabelTextColor = color);
        }

        private void ClearLabelTextColorButton_Click(object sender, RoutedEventArgs e)
        {
            LabelTextColor = null;
        }

        private void WindowBackgroundColorButton_Click(object sender, RoutedEventArgs e)
        {
            ShowColorDialogForSetting("windowbackgroundcolor", color => WindowBackgroundColor = color);
        }

        private void ClearWindowBackgroundColorButton_Click(object sender, RoutedEventArgs e)
        {
            WindowBackgroundColor = null;
        }

        private void ShowColorDialogForSetting(string key, Action<System.Windows.Media.Color> onColorSelected)
        {
            if (_colorSettings.ContainsKey(key))
            {
                var setting = _colorSettings[key];
                ShowColorDialog(setting.Value, setting.Config.DefaultSystemColor, onColorSelected);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveColors(App.ColorsFileName);
            
            // Update all global color brush resources immediately
            foreach (var setting in _colorSettings.Values)
            {
                var brush = setting.Value.HasValue
                    ? new SolidColorBrush(setting.Value.Value)
                    : setting.Config.DefaultSystemBrush;
                ColorHelper.UpdateColorBrush(setting.Config.ResourceKey, brush);
            }
            
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
            // Apply custom font to this window
            FontHandler.ApplyFontToWindow(this);

            LoadColors(App.ColorsFileName);
            
            // Initialize all previews to show system colors if no colors were loaded
            foreach (var key in _colorSettings.Keys)
            {
                UpdatePreview(key);
            }
        }

        private void LoadColors(string path)
        {
            if (!File.Exists(path))
            {
                // Use defaults (leave blank for system colors)
                return;
            }

            try
            {
                var settings = IniFileHandler.LoadIni(path);

                foreach (var key in _colorSettings.Keys)
                {
                    if (settings.TryGetValue(key, out string colorValue))
                    {
                        // Only set if value is not blank/empty
                        if (!string.IsNullOrWhiteSpace(colorValue))
                        {
                            if (ColorHelper.TryParseColor(colorValue, out System.Windows.Media.Color color))
                            {
                                _colorSettings[key].Value = color;
                            }
                        }
                        // If blank, leave color as null (system colors will be used)
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Error loading colors file: " + ex.Message,
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void SaveColors(string path)
        {
            var lines = new List<string>();

            foreach (var setting in _colorSettings.Values)
            {
                if (setting.Value.HasValue)
                {
                    lines.Add($"{setting.Config.Key}={ColorHelper.ColorToString(setting.Value.Value)}");
                }
                else
                {
                    // Save blank value to indicate system colors should be used
                    lines.Add($"{setting.Config.Key}=");
                }
            }

            File.WriteAllLines(path, lines);
        }

    }
}
