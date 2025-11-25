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
        private static readonly Regex BoldItalicPattern = new Regex(@"(\*\*\*|___)(.+?)\1", RegexOptions.Compiled);
        private static readonly Regex BoldPattern = new Regex(@"(?<!\*)\*\*(?!\*)(.+?)\*\*(?!\*)|(?<!_)__(?!_)(.+?)__(?!_)", RegexOptions.Compiled);
        private static readonly Regex ItalicPattern = new Regex(@"(?<!\*)\*(?!\*)(.+?)\*(?!\*)|(?<!_)_(?!_)(.+?)_(?!_)", RegexOptions.Compiled);
        private static readonly Regex StrikethroughPattern = new Regex(@"~~(.+?)~~", RegexOptions.Compiled);
        private static readonly Regex InlineCodePattern = new Regex(@"`([^`]+)`", RegexOptions.Compiled);

        public static void processMarkdown(RichTextBox chatOutput)
        {
            bool insideCodeBlock = false;
            bool insideFourBacktickBlock = false;
            bool insideThinkTag = false;

            // Process each paragraph in order
            foreach (var paragraph in chatOutput.Document.Blocks.OfType<Paragraph>().ToList())
            {
                string paragraphText = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;

                // Check for 4-backtick code block markers first (more specific)
                if (paragraphText.Contains("````"))
                {
                    insideFourBacktickBlock = !insideFourBacktickBlock;
                    // Remove the entire line containing the ```` marker
                    paragraph.Inlines.Clear();
                    continue;
                }

                // Check for 3-backtick code block markers
                if (paragraphText.Contains("```"))
                {
                    insideCodeBlock = !insideCodeBlock;
                    // Remove the entire line containing the ``` marker
                    paragraph.Inlines.Clear();
                    continue;
                }

                // Check for think tag markers (case-insensitive)
                if (paragraphText.IndexOf("<think>", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    insideThinkTag = true;
                    continue;
                }
                if (paragraphText.IndexOf("</think>", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    insideThinkTag = false;
                    continue;
                }

                // Skip processing if we're inside an excluded region
                if (insideCodeBlock || insideFourBacktickBlock || insideThinkTag)
                {
                    // Style code block paragraphs with theme-aware background
                    if (insideCodeBlock)
                    {
                        paragraph.Background = SystemColors.ControlLightBrush;
                    }
                    continue;
                }

                // Process headers first (paragraph-level formatting)
                ProcessHeaders(paragraph);

                // Process inline code blocks first to split them out
                ProcessInlineCodeBlocks(paragraph);

                // Process formatting in order: bold italic, bold, italic, strikethrough
                var formattingProcessors = new[]
                {
                    new FormattingProcessor(BoldItalicPattern, match => new Run(match.Groups[2].Value) { FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic }),
                    new FormattingProcessor(BoldPattern, match => new Run(!string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value) { FontWeight = FontWeights.Bold }),
                    new FormattingProcessor(ItalicPattern, match => new Run(!string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value) { FontStyle = FontStyles.Italic }),
                    new FormattingProcessor(StrikethroughPattern, match => new Run(match.Groups[1].Value) { TextDecorations = TextDecorations.Strikethrough })
                };

                foreach (var processor in formattingProcessors)
                {
                    foreach (var run in paragraph.Inlines.OfType<Run>().ToList())
                    {
                        // Skip already formatted runs
                        if (IsAlreadyFormatted(run))
                            continue;
                            
                        ApplyFormatting(run, processor.Pattern, processor.CreateFormattedRun);
                    }
                }
            }
        }

        private static void ProcessHeaders(Paragraph paragraph)
        {
            string paragraphText = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim();
            
            // Check if this paragraph is a header
            Match headerMatch = HeaderPattern.Match(paragraphText);
            if (headerMatch.Success)
            {
                int headerLevel = headerMatch.Groups[1].Value.Length; // Number of # characters
                string headerText = headerMatch.Groups[2].Value.Trim();
                
                // Set font size based on header level (H1 = 24pt, H2 = 20pt, H3 = 18pt, H4 = 16pt, H5 = 14pt, H6 = 12pt)
                double[] headerSizes = { 24, 20, 18, 16, 14, 12 };
                double fontSize = headerLevel <= 6 ? headerSizes[headerLevel - 1] : 12;
                
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
                // Skip already formatted runs
                if (IsAlreadyFormatted(run))
                    continue;
                    
                if (!InlineCodePattern.IsMatch(run.Text) || !(run.Parent is Paragraph parent))
                    continue;

                var codeMatches = InlineCodePattern.Matches(run.Text);
                var newInlines = new List<Inline>();
                int lastIndex = 0;

                foreach (Match codeMatch in codeMatches)
                {
                    // Text before code block
                    if (codeMatch.Index > lastIndex)
                        newInlines.Add(new Run(run.Text.Substring(lastIndex, codeMatch.Index - lastIndex)));

                    // Code block (unformatted, just the content without backticks)
                    // Wrap in Span with theme-aware background for emphasis
                    var codeRun = new Run(codeMatch.Groups[1].Value);
                    var codeSpan = new Span(codeRun);
                    codeSpan.Background = SystemColors.ControlLightBrush;
                    newInlines.Add(codeSpan);
                    lastIndex = codeMatch.Index + codeMatch.Length;
                }

                // Remaining text
                if (lastIndex < run.Text.Length)
                    newInlines.Add(new Run(run.Text.Substring(lastIndex)));

                // Replace original run
                foreach (var inline in newInlines)
                    parent.Inlines.InsertBefore(run, inline);
                parent.Inlines.Remove(run);
            }
        }

        private static void ApplyFormatting(Run run, Regex pattern, Func<Match, Run> createFormattedRun)
        {
            // Skip formatting if run contains inline code blocks (these should already be split out)
            if (InlineCodePattern.IsMatch(run.Text))
                return;

            // Skip if run is already formatted (has non-default formatting applied)
            if (IsAlreadyFormatted(run))
                return;

            var matches = pattern.Matches(run.Text);
            if (matches.Count == 0 || !(run.Parent is Paragraph parent))
                return;

            var newInlines = BuildInlines(run.Text, matches, createFormattedRun);

            // Replace original run with formatted inlines
            foreach (var inline in newInlines)
                parent.Inlines.InsertBefore(run, inline);
            parent.Inlines.Remove(run);
        }

        private static bool IsAlreadyFormatted(Run run)
        {
            // Check if the run already has formatting applied
            if (run.FontWeight != FontWeights.Normal)
                return true;
            if (run.FontStyle != FontStyles.Normal)
                return true;
            if (run.TextDecorations != null && run.TextDecorations.Count > 0)
                return true;
            if (run.Background != null)
                return true;
            
            // Check if parent Span has formatting
            if (run.Parent is Span parentSpan)
            {
                if (parentSpan.Background != null)
                    return true;
            }
            
            return false;
        }

        private static List<Inline> BuildInlines(string text, MatchCollection matches, Func<Match, Run> createFormattedRun)
        {
            var inlines = new List<Inline>();
            int lastIndex = 0;

            foreach (Match match in matches)
            {
                // Text before match
                if (match.Index > lastIndex)
                    inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));

                // Formatted text (without markers)
                inlines.Add(createFormattedRun(match));
                lastIndex = match.Index + match.Length;
            }

            // Remaining text
            if (lastIndex < text.Length)
                inlines.Add(new Run(text.Substring(lastIndex)));

            return inlines;
        }

        private class FormattingProcessor
        {
            public Regex Pattern { get; private set; }
            public Func<Match, Run> CreateFormattedRun { get; private set; }

            public FormattingProcessor(Regex pattern, Func<Match, Run> createFormattedRun)
            {
                Pattern = pattern;
                CreateFormattedRun = createFormattedRun;
            }
        }
    }
}