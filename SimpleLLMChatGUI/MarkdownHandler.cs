using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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
        // Bold italic link: ***[text](url)*** or ___[text](url)___
        private static readonly Regex BoldItalicLinkPattern = new Regex(@"(?<![a-zA-Z0-9_])(\*\*\*|___)\[([^\[\]]+)\]\(([^()]+)\)\1(?![a-zA-Z0-9_])", RegexOptions.Compiled);
        // Bold link: **[text](url)** or __[text](url)__
        private static readonly Regex BoldLinkPattern = new Regex(@"(?<![a-zA-Z0-9_])(?<!\*)\*\*\[([^\[\]]+)\]\(([^()]+)\)\*\*(?![a-zA-Z0-9_])(?!\*)|(?<![a-zA-Z0-9_])(?<!_)__\[([^\[\]]+)\]\(([^()]+)\)__(?![a-zA-Z0-9_])(?!_)", RegexOptions.Compiled);
        private static readonly Regex LinkPattern = new Regex(@"\[([^\[\]]+)\]\(([^()]+)\)", RegexOptions.Compiled);
        private static readonly Regex BareUrlPattern = new Regex(@"https?://[^\s<>""'()\[\]{}]+(?<![.,;:!?])", RegexOptions.Compiled);
        private static readonly Regex HorizontalRulePattern = new Regex(@"^(?:(?:-\s*){3,}|(?:\*\s*){3,}|(?:_\s*){3,})$", RegexOptions.Compiled);
        private static readonly Regex BacktickFencePattern = new Regex(@"^([^`]*:\s*)?(`{3,})([^`]*)$", RegexOptions.Compiled);

        /// <summary>
        /// H1–H6 size relative to the document base font size.
        /// </summary>
        private static readonly double[] HeaderMultipliers = { 2.0, 1.667, 1.5, 1.333, 1.167, 1.0 };

        /// <summary>
        /// Renders markdown in blocks at or after <paramref name="startBlockIndex"/>, then
        /// advances that index to the document's block count so later passes skip already-handled content.
        /// </summary>
        public static void ProcessMarkdown(FlowDocument document, ref int startBlockIndex)
        {
            if (document == null)
                return;

            List<Block> blocks = document.Blocks.ToList();
            if (startBlockIndex < 0)
                startBlockIndex = 0;
            if (startBlockIndex > blocks.Count)
                startBlockIndex = blocks.Count;

            int activeBacktickFenceLength = 0;

            for (int i = startBlockIndex; i < blocks.Count; i++)
            {
                Paragraph paragraph = blocks[i] as Paragraph;
                if (paragraph == null)
                    continue;

                string paragraphText = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
                string trimmedText = paragraphText.Trim();

                if (TryParseBacktickFence(trimmedText, out int fenceLength, out bool hasInfoString))
                {
                    if (activeBacktickFenceLength == 0)
                    {
                        // Opening fence can include an optional language/info string.
                        activeBacktickFenceLength = fenceLength;
                        int backtickIdx = paragraphText.IndexOf('`');
                        string prefix = backtickIdx > 0 ? paragraphText.Substring(0, backtickIdx).TrimEnd() : null;
                        paragraph.Inlines.Clear();
                        if (!string.IsNullOrEmpty(prefix))
                            paragraph.Inlines.Add(new Run(prefix));
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

                // Skip processing inside fenced code blocks (thinking/tool bodies are
                // BlockUIContainer expanders, not paragraphs, so they are skipped above).
                if (activeBacktickFenceLength > 0)
                {
                    paragraph.SetResourceReference(TextElement.BackgroundProperty, "CodeBlockBackgroundColorBrush");
                    ApplyCodeBlockFont(paragraph);
                    continue;
                }

                // Process horizontal rules as paragraph-level separators
                if (ProcessHorizontalRule(paragraph, trimmedText))
                    continue;

                // Streaming AppendText leaves many Runs per line; merge before regex matching.
                ConsolidateRuns(paragraph);
                trimmedText = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim();

                // Process headers first (paragraph-level formatting)
                ProcessHeaders(paragraph, trimmedText, document);

                // Process inline code blocks first to split them out
                ReplaceInRuns(paragraph, InlineCodePattern, match =>
                {
                    var codeRun = new Run(match.Groups[1].Value);
                    var codeSpan = new Span(codeRun);
                    codeSpan.SetResourceReference(TextElement.BackgroundProperty, "CodeBlockBackgroundColorBrush");
                    codeSpan.SetResourceReference(TextElement.FontFamilyProperty, FontHandler.CodeBlockFontFamilyKey);
                    codeRun.ClearValue(TextElement.FontFamilyProperty);
                    return codeSpan;
                });

                // Emphasized links before plain links (e.g. **[text](url)** / ***[text](url)***).
                ReplaceInRuns(paragraph, BoldItalicLinkPattern, match =>
                    (Inline)CreateHyperlink(match.Groups[2].Value, match.Groups[3].Value.Trim(), FontWeights.Bold, FontStyles.Italic)
                    ?? new Run(match.Value));
                ReplaceInRuns(paragraph, BoldLinkPattern, match =>
                    (Inline)CreateHyperlink(BoldLinkText(match), BoldLinkUrl(match), FontWeights.Bold)
                    ?? new Run(match.Value));
                // Plain links; unwrap [**text**](url) / [***text***](url) into emphasized hyperlinks.
                ReplaceInRuns(paragraph, LinkPattern, match =>
                    (Inline)CreateLinkInline(match.Groups[1].Value, match.Groups[2].Value.Trim()) ?? new Run(match.Value));
                ReplaceInRuns(paragraph, BareUrlPattern, match =>
                    (Inline)CreateHyperlink(match.Value, match.Value) ?? new Run(match.Value));

                // Process formatting in order: bold italic, bold, italic, strikethrough
                ReplaceInRuns(paragraph, BoldItalicPattern, match => new Run(match.Groups[2].Value) { FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic });
                ReplaceInRuns(paragraph, BoldPattern, match => new Run(FirstCapturedGroup(match)) { FontWeight = FontWeights.Bold });
                ReplaceInRuns(paragraph, ItalicPattern, match => new Run(FirstCapturedGroup(match)) { FontStyle = FontStyles.Italic });
                ReplaceInRuns(paragraph, StrikethroughPattern, match => new Run(match.Groups[1].Value) { TextDecorations = TextDecorations.Strikethrough });
            }

            startBlockIndex = blocks.Count;
        }

        /// <summary>
        /// Streaming <see cref="TextRange"/> insertion freezes the chat font onto
        /// each <see cref="Run"/> as a local value; clear those so inlines inherit
        /// the paragraph's <see cref="FontHandler.CodeBlockFontFamilyKey"/> binding.
        /// </summary>
        private static void ApplyCodeBlockFont(Paragraph paragraph)
        {
            if (paragraph == null)
                return;

            paragraph.SetResourceReference(TextElement.FontFamilyProperty, FontHandler.CodeBlockFontFamilyKey);
            foreach (Inline inline in paragraph.Inlines)
                inline.ClearValue(TextElement.FontFamilyProperty);
        }

        private static void ConsolidateRuns(Paragraph paragraph)
        {
            List<Run> runs = paragraph.Inlines.OfType<Run>().ToList();
            if (runs.Count <= 1 || runs.Count != paragraph.Inlines.Count)
                return;

            var sb = new System.Text.StringBuilder();
            foreach (Run run in runs)
                sb.Append(run.Text);

            paragraph.Inlines.Clear();
            paragraph.Inlines.Add(new Run(sb.ToString()));
        }

        private static void ProcessHeaders(Paragraph paragraph, string trimmedText, FlowDocument document)
        {
            Match headerMatch = HeaderPattern.Match(trimmedText);
            if (!headerMatch.Success)
                return;

            int headerLevel = headerMatch.Groups[1].Value.Length; // Number of # characters
            string headerText = headerMatch.Groups[2].Value.Trim();

            // Tag so ApplyHeaderFontSizes can rescale when the chat font size changes.
            paragraph.Tag = headerLevel;
            double baseFontSize = document != null && document.FontSize > 0
                ? document.FontSize
                : FontHandler.GetFontSize();
            paragraph.FontSize = baseFontSize * HeaderMultipliers[Math.Min(headerLevel, 6) - 1];
            paragraph.FontWeight = FontWeights.Bold;

            // Replace the paragraph content with just the header text (without the # markers)
            // This allows inline formatting to be processed on the header text afterwards
            paragraph.Inlines.Clear();
            paragraph.Inlines.Add(new Run(headerText));
        }

        /// <summary>
        /// Recomputes markdown header font sizes from the document base size.
        /// Needed because headers store an absolute <see cref="TextElement.FontSize"/> that
        /// does not inherit when <see cref="FlowDocument.FontSize"/> changes.
        /// </summary>
        public static void ApplyHeaderFontSizes(FlowDocument document, double baseFontSize)
        {
            if (document == null || baseFontSize <= 0)
                return;

            foreach (Block block in document.Blocks)
            {
                Paragraph paragraph = block as Paragraph;
                if (paragraph == null || !(paragraph.Tag is int))
                    continue;

                int headerLevel = (int)paragraph.Tag;
                if (headerLevel < 1 || headerLevel > HeaderMultipliers.Length)
                    continue;

                paragraph.FontSize = baseFontSize * HeaderMultipliers[headerLevel - 1];
            }
        }

        private static void ReplaceInRuns(Paragraph paragraph, Regex pattern, Func<Match, Inline> createInline)
        {
            foreach (var run in paragraph.Inlines.OfType<Run>().ToList())
            {
                if (!pattern.IsMatch(run.Text) || !(run.Parent is Paragraph parent))
                    continue;

                var newInlines = BuildInlines(run.Text, pattern.Matches(run.Text), createInline);

                foreach (var inline in newInlines)
                    parent.Inlines.InsertBefore(run, inline);
                parent.Inlines.Remove(run);
            }
        }

        private static Hyperlink CreateLinkInline(string displayText, string url)
        {
            string text = displayText;
            FontWeight fontWeight = FontWeights.Normal;
            FontStyle fontStyle = FontStyles.Normal;

            if (TryStripWrappedMarkers(ref text, "***") || TryStripWrappedMarkers(ref text, "___"))
            {
                fontWeight = FontWeights.Bold;
                fontStyle = FontStyles.Italic;
            }
            else if (TryStripWrappedMarkers(ref text, "**") || TryStripWrappedMarkers(ref text, "__"))
            {
                fontWeight = FontWeights.Bold;
            }

            return CreateHyperlink(text, url, fontWeight, fontStyle);
        }

        private static bool TryStripWrappedMarkers(ref string text, string marker)
        {
            if (text == null || marker == null || text.Length <= marker.Length * 2)
                return false;
            if (!text.StartsWith(marker, StringComparison.Ordinal) ||
                !text.EndsWith(marker, StringComparison.Ordinal))
                return false;

            text = text.Substring(marker.Length, text.Length - marker.Length * 2);
            return true;
        }

        private static string BoldLinkText(Match match)
        {
            return !string.IsNullOrEmpty(match.Groups[1].Value)
                ? match.Groups[1].Value
                : match.Groups[3].Value;
        }

        private static string BoldLinkUrl(Match match)
        {
            return !string.IsNullOrEmpty(match.Groups[2].Value)
                ? match.Groups[2].Value.Trim()
                : match.Groups[4].Value.Trim();
        }

        private static Hyperlink CreateHyperlink(
            string displayText,
            string url,
            FontWeight fontWeight = default(FontWeight),
            FontStyle fontStyle = default(FontStyle))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return null;

            if (fontWeight == default(FontWeight))
                fontWeight = FontWeights.Normal;
            if (fontStyle == default(FontStyle))
                fontStyle = FontStyles.Normal;

            var hyperlink = new Hyperlink(new Run(displayText))
            {
                NavigateUri = uri,
                Cursor = Cursors.Hand,
                FontWeight = fontWeight,
                FontStyle = fontStyle
            };
            hyperlink.SetResourceReference(TextElement.ForegroundProperty, "ChatTextColorBrush");
            AttachTooltip(hyperlink, uri.AbsoluteUri);
            hyperlink.PreviewMouseLeftButtonDown += OnHyperlinkClick;
            return hyperlink;
        }

        private static void AttachTooltip(Hyperlink hyperlink, string text)
        {
            var tooltip = new ToolTip { Content = text, Placement = PlacementMode.Mouse };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, _) => { timer.Stop(); tooltip.IsOpen = true; };
            hyperlink.MouseEnter += (s, _) => timer.Start();
            hyperlink.MouseLeave += (s, _) => { timer.Stop(); tooltip.IsOpen = false; };
        }

        private static void OnHyperlinkClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Hyperlink hyperlink && hyperlink.NavigateUri != null)
            {
                Process.Start(new ProcessStartInfo(hyperlink.NavigateUri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
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

            fenceLength = match.Groups[2].Value.Length;
            hasInfoString = !string.IsNullOrWhiteSpace(match.Groups[3].Value);
            return true;
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

    }
}