using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SimpleLLMChatGUI
{
    public class MarkdownHandler
    {
        private static readonly Regex BoldItalicPattern = new Regex(@"(\*\*\*|___)(.+?)\1", RegexOptions.Compiled);
        private static readonly Regex BoldPattern = new Regex(@"(?<!\*)\*\*(?!\*)(.+?)\*\*(?!\*)|(?<!_)__(?!_)(.+?)__(?!_)", RegexOptions.Compiled);
        private static readonly Regex ItalicPattern = new Regex(@"(?<!\*)\*(?!\*)(.+?)\*(?!\*)|(?<!_)_(?!_)(.+?)_(?!_)", RegexOptions.Compiled);
        private static readonly Regex StrikethroughPattern = new Regex(@"~~(.+?)~~", RegexOptions.Compiled);

        public static void processMarkdown(RichTextBox chatOutput)
        {
            bool insideCodeBlock = false;
            bool insideThinkTag = false;

            // Process each paragraph in order
            foreach (var paragraph in chatOutput.Document.Blocks.OfType<Paragraph>().ToList())
            {
                string paragraphText = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;

                // Check for code block markers
                if (paragraphText.Contains("```"))
                {
                    insideCodeBlock = !insideCodeBlock;
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
                if (insideCodeBlock || insideThinkTag)
                    continue;

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
                        ApplyFormatting(run, processor.Pattern, processor.CreateFormattedRun);
                    }
                }
            }
        }

        private static void ApplyFormatting(Run run, Regex pattern, Func<Match, Run> createFormattedRun)
        {
            var matches = pattern.Matches(run.Text);
            if (matches.Count == 0 || !(run.Parent is Paragraph parent))
                return;

            var newInlines = BuildInlines(run.Text, matches, createFormattedRun);

            // Replace original run with formatted inlines
            foreach (var inline in newInlines)
                parent.Inlines.InsertBefore(run, inline);
            parent.Inlines.Remove(run);
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