using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SimpleLLMChatGUI
{
    public partial class Options
    {
        private readonly List<string> _availableTools;
        private readonly List<string> _systemFonts;
        private List<ToolOptionDefinition> _toolOptions;
        private readonly Dictionary<string, FrameworkElement> _toolOptionControls = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        private List<ScrollViewer> _toolGroupPages = new List<ScrollViewer>();
        private readonly Dictionary<string, TextBox> _toolTimeoutControls = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ToolModeRow> _toolModeRows = new List<ToolModeRow>();

        /// <summary>Per-tool setting: whether the tool is enabled and, if so, how calls are approved.</summary>
        private enum ToolModeChoice
        {
            AlwaysRun,
            Auto,
            Manual,
            Off
        }

        /// <summary>One tool's Always Run / Auto / Manual / Off choice on the Tools page.</summary>
        private sealed class ToolModeRow
        {
            public string ToolName;
            public RadioButton AlwaysRun;
            public RadioButton Auto;
            public RadioButton Manual;
            public RadioButton Off;

            // Approval mode from config, kept for tools set to Off so re-enabling
            // a tool later does not silently drop its approval requirement.
            public ToolModeChoice StoredApproval;

            public ToolModeChoice Selected
            {
                get
                {
                    if (Off.IsChecked == true) return ToolModeChoice.Off;
                    if (Auto.IsChecked == true) return ToolModeChoice.Auto;
                    if (Manual.IsChecked == true) return ToolModeChoice.Manual;
                    return ToolModeChoice.AlwaysRun;
                }
            }

            /// <summary>Approval mode to save: the selected one, or the stored one when Off.</summary>
            public ToolModeChoice ApprovalToSave
            {
                get
                {
                    ToolModeChoice mode = Selected;
                    return mode == ToolModeChoice.Off ? StoredApproval : mode;
                }
            }

            public void Select(ToolModeChoice choice)
            {
                AlwaysRun.IsChecked = choice == ToolModeChoice.AlwaysRun;
                Auto.IsChecked = choice == ToolModeChoice.Auto;
                Manual.IsChecked = choice == ToolModeChoice.Manual;
                Off.IsChecked = choice == ToolModeChoice.Off;
            }
        }

        private static string GetBareToolName(string displayOrName)
        {
            if (string.IsNullOrEmpty(displayOrName))
                return displayOrName;
            int slash = displayOrName.LastIndexOf('/');
            return slash >= 0 ? displayOrName.Substring(slash + 1) : displayOrName;
        }

        private List<string> GetToolSettings()
        {
            var toolLines = new List<string>
            {
                AppSettings.EnabledTools.Line(string.Join(",", _toolModeRows.Where(r => r.Selected != ToolModeChoice.Off).Select(r => r.ToolName))),
                AppSettings.ToolsRequiringApproval.Line(string.Join(",", _toolModeRows.Where(r => r.ApprovalToSave == ToolModeChoice.Manual).Select(r => r.ToolName))),
                AppSettings.AutoApprovalTools.Line(string.Join(",", _toolModeRows.Where(r => r.ApprovalToSave == ToolModeChoice.Auto).Select(r => r.ToolName))),
                AppSettings.JevBaseUrl.Line(JevBaseUrl ?? string.Empty),
                AppSettings.JevApiKey.Line(JevApiKey ?? string.Empty),
                AppSettings.JevModel.Line(JevModel ?? string.Empty),
                AppSettings.ShowJevConfidence.Line(ShowJevConfidence ? "1" : "0"),
                AppSettings.MaxConsecutiveFailedToolCalls.Line(MaxConsecutiveFailedToolCalls),
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

        private void BuildToolOptionsUI(ConfigHandler config)
        {
            _toolOptionControls.Clear();
            _toolGroupPages.Clear();

            if (_toolOptions.Count == 0)
                return;

            foreach (var group in GetToolOptionGroups())
            {
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
                    string configValue = config.GetConfigValue(opt.Name);
                    string currentValue = string.IsNullOrEmpty(configValue) ? opt.Default : configValue;
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
                        string configValue = config.GetConfigValue(opt.Name);
                        string currentValue = string.IsNullOrEmpty(configValue) ? opt.Default : configValue;
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
                CategoryListBox.Items.Add(new ListBoxItem
                {
                    Content = group.Key,
                    Tag = scrollViewer
                });
            }
        }

        private static void RestrictToDigits(TextBox textBox)
        {
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
        }

        private IOrderedEnumerable<IGrouping<string, ToolOptionDefinition>> GetToolOptionGroups()
        {
            return _toolOptions
                .GroupBy(opt => string.IsNullOrWhiteSpace(opt.Source) ? "Tools" : opt.Source, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds a row per tool with Always Run / Auto / Ask Me / Off and a timeout box,
        /// and loads each from config. A tool on both approval lists loads as Auto.
        /// </summary>
        private void BuildToolModesUI(ConfigHandler config)
        {
            _toolModeRows.Clear();
            _toolTimeoutControls.Clear();
            ToolModeRowsStack.Children.Clear();

            if (_availableTools.Count == 0)
            {
                var empty = new TextBlock { Text = "No tools installed.", Margin = new Thickness(2, 4, 2, 4) };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "LabelTextColorBrush");
                ToolModeRowsStack.Children.Add(empty);
                return;
            }

            var enabledTools = new HashSet<string>(config.GetList(AppSettings.EnabledTools), StringComparer.OrdinalIgnoreCase);
            var autoTools = new HashSet<string>(config.GetList(AppSettings.AutoApprovalTools), StringComparer.OrdinalIgnoreCase);
            var manualTools = new HashSet<string>(config.GetList(AppSettings.ToolsRequiringApproval), StringComparer.OrdinalIgnoreCase);

            foreach (string displayName in _availableTools)
            {
                string toolName = GetBareToolName(displayName);
                string group = "toolmode_" + _toolModeRows.Count;

                var line = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (int i = 0; i < 6; i++)
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock
                {
                    Text = displayName,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = displayName,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 8, 0)
                };
                name.SetResourceReference(TextBlock.ForegroundProperty, "LabelTextColorBrush");
                line.Children.Add(name);

                var row = new ToolModeRow
                {
                    ToolName = toolName,
                    AlwaysRun = MakeToolModeChoice("Always Run", group, line, 1),
                    Auto = MakeToolModeChoice("Auto", group, line, 2),
                    Manual = MakeToolModeChoice("Ask Me", group, line, 3),
                    Off = MakeToolModeChoice("Off", group, line, 4)
                };

                if (autoTools.Contains(toolName))
                    row.StoredApproval = ToolModeChoice.Auto;
                else if (manualTools.Contains(toolName))
                    row.StoredApproval = ToolModeChoice.Manual;
                else
                    row.StoredApproval = ToolModeChoice.AlwaysRun;

                bool enabled = enabledTools.Contains(toolName) || enabledTools.Contains(displayName);
                row.Select(enabled ? row.StoredApproval : ToolModeChoice.Off);

                string timeout = config.GetConfigValue("tooltimeout." + toolName.ToLowerInvariant());
                var timeoutLabel = new TextBlock
                {
                    Text = "Timeout (s):",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(14, 0, 4, 0),
                    ToolTip = TimeoutToolTip
                };
                timeoutLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelTextColorBrush");
                Grid.SetColumn(timeoutLabel, 5);
                line.Children.Add(timeoutLabel);

                var timeoutBox = new TextBox
                {
                    Text = string.IsNullOrEmpty(timeout) ? "0" : timeout,
                    Width = 40,
                    Height = 21,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Right,
                    ToolTip = TimeoutToolTip
                };
                RestrictToDigits(timeoutBox);
                Grid.SetColumn(timeoutBox, 6);
                line.Children.Add(timeoutBox);
                _toolTimeoutControls[toolName] = timeoutBox;

                ToolModeRowsStack.Children.Add(line);
                _toolModeRows.Add(row);
            }
        }

        private const string TimeoutToolTip = "Timeout in seconds; 0 or blank = no timeout";

        private static RadioButton MakeToolModeChoice(string text, string group, Grid line, int column)
        {
            var choice = new RadioButton
            {
                Content = text,
                GroupName = group,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(choice, column);
            line.Children.Add(choice);
            return choice;
        }

        private void SetAllToolModes_Click(object sender, RoutedEventArgs e)
        {
            string tag = ((FrameworkElement)sender).Tag as string;
            ToolModeChoice choice = tag == "auto" ? ToolModeChoice.Auto
                : tag == "manual" ? ToolModeChoice.Manual
                : tag == "off" ? ToolModeChoice.Off
                : ToolModeChoice.AlwaysRun;
            foreach (ToolModeRow row in _toolModeRows)
                row.Select(choice);
        }
    }
}
