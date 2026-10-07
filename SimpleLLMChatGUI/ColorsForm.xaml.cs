using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Forms;

namespace SimpleLLMChatGUI
{
    public partial class ColorsForm : Window
    {
        public class ColorSetting : INotifyPropertyChanged
        {
            private System.Windows.Media.Color? _value;

            public ColorSetting(string key, string label)
            {
                Config = ColorHelper.ColorConfigs[key];
                Label = label;
            }

            public ColorConfig Config { get; set; }
            public string Label { get; private set; }
            public System.Windows.Media.Color? Value
            {
                get { return _value; }
                set
                {
                    _value = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewBrush)));
                }
            }
            public Brush PreviewBrush
            {
                get { return new SolidColorBrush(Value ?? Config.DefaultSystemColor); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private readonly List<ColorSetting> _colorSettings = new List<ColorSetting>
        {
            new ColorSetting("buttontextcolor", "Button Text"),
            new ColorSetting("chatbackgroundcolor", "Chat BG"),
            new ColorSetting("chattextcolor", "Chat Text"),
            new ColorSetting("codeblockbackgroundcolor", "Code Block BG"),
            new ColorSetting("labeltextcolor", "Label Text"),
            new ColorSetting("windowbackgroundcolor", "Window BG")
        };

        public ColorsForm()
        {
            InitializeComponent();
            ColorRows.ItemsSource = _colorSettings;
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

        private void ChooseColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.DataContext is ColorSetting setting)
            {
                ShowColorDialog(setting.Value, setting.Config.DefaultSystemColor, color => setting.Value = color);
            }
        }

        private void ClearColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.DataContext is ColorSetting setting)
            {
                setting.Value = null;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveColors(App.ColorsFileName);
            
            // Update all global color brush resources immediately
            foreach (var setting in _colorSettings)
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

                foreach (var setting in _colorSettings)
                {
                    if (settings.TryGetValue(setting.Config.Key, out string colorValue))
                    {
                        // Only set if value is not blank/empty
                        if (!string.IsNullOrWhiteSpace(colorValue))
                        {
                            if (ColorHelper.TryParseColor(colorValue, out System.Windows.Media.Color color))
                            {
                                setting.Value = color;
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

            foreach (var setting in _colorSettings)
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
