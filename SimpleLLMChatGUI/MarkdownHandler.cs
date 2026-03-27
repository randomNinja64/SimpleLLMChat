using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SimpleLLMChatGUI
{
    public class MarkdownHandler
    {
        private static readonly Regex HeaderPattern = new Regex(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);
        // Bold italic: requires word boundaries (not adjacent to word characters including underscore)
        private static readonly Regex BoldItalicPattern = new Regex(@"(?<![a-zA-Z0-9_])(\*\*\*|___)(.+?)\1(?![a-zA-Z0-9_])", RegexOptions.Compiled);
        // Bold: **text** or __text__, not adjacent to word characters or the same marker character
        private static readonly Regex BoldPattern = new Regex(@"(?<![a-zA-Z0-9_])(?<!\*)\*\*(.+?)\*\*(?![a-zA-Z0-9_])(?!\*)|(?<![a-zA-Z0-9_])(?<!_)__(.+?)__(?![a-zA-Z0-9_])(?!_)", RegexOptions.Compiled);
        // Italic: *text* or _text_, not adjacent to word characters or the same marker character
        private static readonly Regex ItalicPattern = new Regex(@"(?<![a-zA-Z0-9_])(?<!\*)\*(.+?)\*(?![a-zA-Z0-9_])(?!\*)|(?<![a-zA-Z0-9_])(?<!_)_(.+?)_(?![a-zA-Z0-9_])(?!_)", RegexOptions.Compiled);
        private static readonly Regex StrikethroughPattern = new Regex(@"~~(.+?)~~", RegexOptions.Compiled);
        private static readonly Regex InlineCodePattern = new Regex(@"`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex HorizontalRulePattern = new Regex(@"^(?:(?:-\s*){3,}|(?:\*\s*){3,}|(?:_\s*){3,})$", RegexOptions.Compiled);
        private static readonly Regex BacktickFencePattern = new Regex(@"^(`{3,})([^`]*)$", RegexOptions.Compiled);

        public static void processMarkdown(RichTextBox chatOutput)
        {
            int activeBacktickFenceLength = 0;
            bool insideThinkTag = false;

            // Process each paragraph in order
            foreach (var paragraph in chatOutput.Document.Blocks.OfType<Paragraph>().ToList())
            {
                string paragraphText = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;

                string trimmedText = paragraphText.Trim();
                if (TryParseBacktickFence(trimmedText, out int fenceLength, out bool hasInfoString))
                {
                    if (activeBacktickFenceLength == 0)
                    {
                        // Opening fence can include an optional language/info string.
                        activeBacktickFenceLength = fenceLength;
                        paragraph.Inlines.Clear();
                        continue;
                    }

                    // Closing fence must be backticks only and at least the opening fence length.
                    if (!hasInfoString && fenceLength >= activeBacktickFenceLength)
                    {
                        activeBacktickFenceLength = 0;
                        paragraph.Inlines.Clear();
                        continue;
                    }
                }

                // Check for think tag markers (case-insensitive)
                if (paragraphText.IndexOf("<think>", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    insideThinkTag = true;
                    continue;
                }
                if (paragraphText.IndexOf("</think>", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    insideThinkTag = false;
                    continue;
                }

                // Skip processing if we're inside an excluded region
                if (activeBacktickFenceLength > 0 || insideThinkTag)
                {
                    if (activeBacktickFenceLength > 0)
                    {
                        ApplyCodeBlockStyle(paragraph);
                    }
                    continue;
                }

                // Process horizontal rules as paragraph-level separators
                if (ProcessHorizontalRule(paragraph, trimmedText))
                    continue;

                // Process headers first (paragraph-level formatting)
                ProcessHeaders(paragraph, trimmedText);

                // Process inline code blocks first to split them out
                ProcessInlineCodeBlocks(paragraph);

                // Process formatting in order: bold italic, bold, italic, strikethrough
                var formattingProcessors = new[]
                {
                    new FormattingProcessor(BoldItalicPattern, match => new Run(match.Groups[2].Value) { FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic }),
                    new FormattingProcessor(BoldPattern, match => new Run(FirstCapturedGroup(match)) { FontWeight = FontWeights.Bold }),
                    new FormattingProcessor(ItalicPattern, match => new Run(FirstCapturedGroup(match)) { FontStyle = FontStyles.Italic }),
                    new FormattingProcessor(StrikethroughPattern, match => new Run(match.Groups[1].Value) { TextDecorations = TextDecorations.Strikethrough })
                };

                foreach (var processor in formattingProcessors)
                {
                    foreach (var run in paragraph.Inlines.OfType<Run>().ToList())
                    {
                        ApplyFormatting(run, processor.Pattern, processor.CreateFormattedRun);
                    }
                }
            }
        }

        private static void ProcessHeaders(Paragraph paragraph, string trimmedText)
        {
            Match headerMatch = HeaderPattern.Match(trimmedText);
            if (headerMatch.Success)
            {
                int headerLevel = headerMatch.Groups[1].Value.Length; // Number of # characters
                string headerText = headerMatch.Groups[2].Value.Trim();
                
                double[] headerMultipliers = { 2.0, 1.667, 1.5, 1.333, 1.167, 1.0 };
                double fontSize = FontHandler.GetFontSize() * headerMultipliers[Math.Min(headerLevel, 6) - 1];
                
                paragraph.FontSize = fontSize;
                paragraph.FontWeight = FontWeights.Bold;
                
                // Replace the paragraph content with just the header text (without the # markers)
                // This allows inline formatting to be processed on the header text afterwards
                paragraph.Inlines.Clear();
                paragraph.Inlines.Add(new Run(headerText));
            }
        }

        private static void ProcessInlineCodeBlocks(Paragraph paragraph)
        {
            foreach (var run in paragraph.Inlines.OfType<Run>().ToList())
            {
                if (!InlineCodePattern.IsMatch(run.Text) || !(run.Parent is Paragraph parent))
                    continue;

                var newInlines = BuildInlines(run.Text, InlineCodePattern.Matches(run.Text), match =>
                {
                    var codeSpan = new Span(new Run(match.Groups[1].Value));
                    codeSpan.Background = GetCodeBlockBrush();
                    return codeSpan;
                });

                foreach (var inline in newInlines)
                    parent.Inlines.InsertBefore(run, inline);
                parent.Inlines.Remove(run);
            }
        }

        private static bool ProcessHorizontalRule(Paragraph paragraph, string trimmedText)
        {
            if (!HorizontalRulePattern.IsMatch(trimmedText))
                return false;

            Brush separatorBrush = paragraph.Foreground
                ?? (Application.Current?.MainWindow?.Foreground as Brush)
                ?? SystemColors.ControlTextBrush;

            paragraph.Inlines.Clear();
            paragraph.Margin = new Thickness(0, 3, 0, 3);
            paragraph.BorderBrush = separatorBrush;
            paragraph.BorderThickness = new Thickness(0, 1, 0, 0);
            paragraph.Padding = new Thickness(0);
            paragraph.LineHeight = 1;
            paragraph.FontSize = 1;
            paragraph.Inlines.Add(new Run(" "));

            return true;
        }

        private static bool TryParseBacktickFence(string trimmedText, out int fenceLength, out bool hasInfoString)
        {
            fenceLength = 0;
            hasInfoString = false;

            Match match = BacktickFencePattern.Match(trimmedText);
            if (!match.Success)
                return false;

            fenceLength = match.Groups[1].Value.Length;
            hasInfoString = !string.IsNullOrWhiteSpace(match.Groups[2].Value);
            return true;
        }

        private static Brush GetCodeBlockBrush()
        {
            return Application.Current.Resources["CodeBlockBackgroundColorBrush"] as Brush ?? SystemColors.ControlBrush;
        }

        private static void ApplyCodeBlockStyle(Paragraph paragraph)
        {
            paragraph.Background = GetCodeBlockBrush();
        }

        private static void ApplyFormatting(Run run, Regex pattern, Func<Match, Inline> createFormattedInline)
        {
            if (InlineCodePattern.IsMatch(run.Text))
                return;

            var matches = pattern.Matches(run.Text);
            if (matches.Count == 0 || !(run.Parent is Paragraph parent))
                return;

            var newInlines = BuildInlines(run.Text, matches, createFormattedInline);

            // Replace original run with formatted inlines
            foreach (var inline in newInlines)
                parent.Inlines.InsertBefore(run, inline);
            parent.Inlines.Remove(run);
        }

        private static string FirstCapturedGroup(Match match)
        {
            return !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value;
        }

        private static List<Inline> BuildInlines(string text, MatchCollection matches, Func<Match, Inline> createFormattedInline)
        {
            var inlines = new List<Inline>();
            int lastIndex = 0;

            foreach (Match match in matches)
            {
                if (match.Index > lastIndex)
                    inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));

                inlines.Add(createFormattedInline(match));
                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
                inlines.Add(new Run(text.Substring(lastIndex)));

            return inlines;
        }

        private class FormattingProcessor
        {
            public Regex Pattern { get; private set; }
            public Func<Match, Inline> CreateFormattedRun { get; private set; }

            public FormattingProcessor(Regex pattern, Func<Match, Inline> createFormattedRun)
            {
                Pattern = pattern;
                CreateFormattedRun = createFormattedRun;
            }
        }
    }
}