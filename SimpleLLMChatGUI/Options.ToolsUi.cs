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

        private List<string> GetSelectedToolsFromListBox(ListBox listBox)
        {
            return listBox.SelectedItems
                .OfType<string>()
                .Select(GetBareToolName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
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
                var listBoxItem = new ListBoxItem
                {
                    Content = group.Key
                };
                CategoryListBox.Items.Add(listBoxItem);

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
            }
        }

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

        private IOrderedEnumerable<IGrouping<string, ToolOptionDefinition>> GetToolOptionGroups()
        {
            return _toolOptions
                .GroupBy(opt => string.IsNullOrWhiteSpace(opt.Source) ? "Tools" : opt.Source, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        }

        private void LoadToolSelections(ConfigHandler config)
        {
            ApplyToolSelectionToListBox(ToolsListBox, config.GetConfigList("tools"));
            ApplyToolSelectionToListBox(
                ToolsRequiringApprovalListBox,
                config.GetConfigList("toolsrequiringapproval"));
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
