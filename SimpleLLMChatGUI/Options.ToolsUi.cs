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
        private readonly List<ToolApprovalRow> _toolApprovalRows = new List<ToolApprovalRow>();

        private enum ApprovalChoice
        {
            Auto,
            Manual,
            None
        }

        /// <summary>One tool's Auto / Manual / None choice on the Tool Approvals page.</summary>
        private sealed class ToolApprovalRow
        {
            public string ToolName;
            public RadioButton Auto;
            public RadioButton Manual;
            public RadioButton None;

            public void Select(ApprovalChoice choice)
            {
                Auto.IsChecked = choice == ApprovalChoice.Auto;
                Manual.IsChecked = choice == ApprovalChoice.Manual;
                None.IsChecked = choice == ApprovalChoice.None;
            }
        }

        private List<string> GetSelectedToolsFromListBox(ListBox listBox)
        {
            return listBox.SelectedItems
                .OfType<string>()
                .Select(GetBareToolName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        private void SelectAllTools_Click(object sender, RoutedEventArgs e)
        {
            var listBox = ((FrameworkElement)sender).Tag as ListBox;
            if (listBox != null)
                listBox.SelectAll();
        }

        private void SelectNoTools_Click(object sender, RoutedEventArgs e)
        {
            var listBox = ((FrameworkElement)sender).Tag as ListBox;
            if (listBox != null)
                listBox.UnselectAll();
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
                AppSettings.EnabledTools.Line(string.Join(",", GetSelectedToolsFromListBox(ToolsListBox))),
                AppSettings.ToolsRequiringApproval.Line(string.Join(",", GetApprovalTools(ApprovalChoice.Manual))),
                AppSettings.AutoApprovalTools.Line(string.Join(",", GetApprovalTools(ApprovalChoice.Auto))),
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

        private void BuildToolTimeoutsUI(ConfigHandler config)
        {
            _toolTimeoutControls.Clear();

            if (_availableTools.Count == 0)
                return;

            var stack = ToolTimeoutsStack;
            if (stack == null)
                return;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            foreach (string displayName in _availableTools)
            {
                string toolName = GetBareToolName(displayName);
                int rowIdx = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = displayName,
                    Padding = new Thickness(0, 2, 4, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "LabelTextColorBrush");
                Grid.SetRow(label, rowIdx);
                Grid.SetColumn(label, 0);

                string configVal = config.GetConfigValue("tooltimeout." + toolName.ToLowerInvariant());
                if (string.IsNullOrEmpty(configVal))
                    configVal = "0";
                var textBox = new TextBox
                {
                    Text = configVal,
                    Height = 23,
                    Margin = new Thickness(0, 2, 0, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                RestrictToDigits(textBox);
                Grid.SetRow(textBox, rowIdx);
                Grid.SetColumn(textBox, 1);

                grid.Children.Add(label);
                grid.Children.Add(textBox);
                _toolTimeoutControls[toolName] = textBox;
            }

            stack.Children.Add(grid);
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

        private void LoadToolSelections(ConfigHandler config)
        {
            ApplyToolSelectionToListBox(ToolsListBox, config.GetList(AppSettings.EnabledTools));
        }

        /// <summary>
        /// Builds a row per tool with Auto / Manual / None and loads each from config.
        /// A tool on both lists loads as Auto.
        /// </summary>
        private void BuildToolApprovalsUI(ConfigHandler config)
        {
            _toolApprovalRows.Clear();
            ToolApprovalRowsStack.Children.Clear();

            if (_availableTools.Count == 0)
            {
                var empty = new TextBlock { Text = "No tools installed.", Margin = new Thickness(2, 4, 2, 4) };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "LabelTextColorBrush");
                ToolApprovalRowsStack.Children.Add(empty);
                return;
            }

            var autoTools = new HashSet<string>(config.GetList(AppSettings.AutoApprovalTools), StringComparer.OrdinalIgnoreCase);
            var manualTools = new HashSet<string>(config.GetList(AppSettings.ToolsRequiringApproval), StringComparer.OrdinalIgnoreCase);

            foreach (string displayName in _availableTools)
            {
                string toolName = GetBareToolName(displayName);
                string group = "approval_" + _toolApprovalRows.Count;

                var line = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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

                var row = new ToolApprovalRow
                {
                    ToolName = toolName,
                    Auto = MakeApprovalChoice("Auto", group, line, 1),
                    Manual = MakeApprovalChoice("Manual", group, line, 2),
                    None = MakeApprovalChoice("None", group, line, 3)
                };

                if (autoTools.Contains(toolName))
                    row.Select(ApprovalChoice.Auto);
                else if (manualTools.Contains(toolName))
                    row.Select(ApprovalChoice.Manual);
                else
                    row.Select(ApprovalChoice.None);

                ToolApprovalRowsStack.Children.Add(line);
                _toolApprovalRows.Add(row);
            }
        }

        private static RadioButton MakeApprovalChoice(string text, string group, Grid line, int column)
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

        private void SetAllApprovalModes_Click(object sender, RoutedEventArgs e)
        {
            string tag = ((FrameworkElement)sender).Tag as string;
            ApprovalChoice choice = tag == "auto" ? ApprovalChoice.Auto
                : tag == "manual" ? ApprovalChoice.Manual
                : ApprovalChoice.None;
            foreach (ToolApprovalRow row in _toolApprovalRows)
                row.Select(choice);
        }

        private List<string> GetApprovalTools(ApprovalChoice choice)
        {
            var tools = new List<string>();
            foreach (ToolApprovalRow row in _toolApprovalRows)
            {
                RadioButton button = choice == ApprovalChoice.Auto ? row.Auto : row.Manual;
                if (button.IsChecked == true)
                    tools.Add(row.ToolName);
            }
            return tools;
        }

        private void ApplyToolSelectionToListBox(ListBox listBox, List<string> tools)
        {
            if (listBox == null || tools == null)
                return;

            var selectedTools = new HashSet<string>(tools, StringComparer.OrdinalIgnoreCase);
            foreach (var item in listBox.Items)
            {
                var displayName = item as string;
                if (displayName == null)
                    continue;
                if (selectedTools.Contains(displayName) || selectedTools.Contains(GetBareToolName(displayName)))
                    listBox.SelectedItems.Add(item);
            }
        }
    }
}
