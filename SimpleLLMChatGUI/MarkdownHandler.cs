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

                // Process bold italic first (*** or ___)
                foreach (var run in paragraph.Inlines.OfType<Run>().ToList())
                {
                    ApplyBoldItalicFormatting(run);
                }

                // Process bold formatting
                foreach (var run in paragraph.Inlines.OfType<Run>().ToList())
                {
                    ApplyBoldFormatting(run);
                }

                // Process italic formatting (on potentially modified runs)
                foreach (var run in paragraph.Inlines.OfType<Run>().ToList())
                {
                    ApplyItalicFormatting(run);
                }

                // Process strikethrough formatting
                foreach (var run in paragraph.Inlines.OfType<Run>().ToList())
                {
                    ApplyStrikethroughFormatting(run);
                }
            }
        }

        private static void ApplyBoldItalicFormatting(Run run)
        {
            var matches = BoldItalicPattern.Matches(run.Text);
            if (matches.Count == 0 || !(run.Parent is Paragraph parent))
                return;

            var newInlines = BuildBoldItalicInlines(run.Text, matches);

            // Replace original run with formatted inlines
            foreach (var inline in newInlines)
                parent.Inlines.InsertBefore(run, inline);
            parent.Inlines.Remove(run);
        }

        private static void ApplyBoldFormatting(Run run)
        {
            var matches = BoldPattern.Matches(run.Text);
            if (matches.Count == 0 || !(run.Parent is Paragraph parent))
                return;

            var newInlines = BuildBoldInlines(run.Text, matches);

            // Replace original run with formatted inlines
            foreach (var inline in newInlines)
                parent.Inlines.InsertBefore(run, inline);
            parent.Inlines.Remove(run);
        }

        private static void ApplyItalicFormatting(Run run)
        {
            var matches = ItalicPattern.Matches(run.Text);
            if (matches.Count == 0 || !(run.Parent is Paragraph parent))
                return;

            var newInlines = BuildItalicInlines(run.Text, matches);

            // Replace original run with formatted inlines
            foreach (var inline in newInlines)
                parent.Inlines.InsertBefore(run, inline);
            parent.Inlines.Remove(run);
        }

        private static void ApplyStrikethroughFormatting(Run run)
        {
            var matches = StrikethroughPattern.Matches(run.Text);
            if (matches.Count == 0 || !(run.Parent is Paragraph parent))
                return;

            var newInlines = BuildStrikethroughInlines(run.Text, matches);

            // Replace original run with formatted inlines
            foreach (var inline in newInlines)
                parent.Inlines.InsertBefore(run, inline);
            parent.Inlines.Remove(run);
        }

        private static List<Inline> BuildBoldInlines(string text, MatchCollection matches)
        {
            var inlines = new List<Inline>();
            int lastIndex = 0;

            foreach (Match match in matches)
            {
                // Text before match
                if (match.Index > lastIndex)
                    inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));

                // Bold text (without markers) - check which group matched (** or __)
                string boldText = !string.IsNullOrEmpty(match.Groups[1].Value) 
                    ? match.Groups[1].Value 
                    : match.Groups[2].Value;
                inlines.Add(new Run(boldText) { FontWeight = FontWeights.Bold });
                lastIndex = match.Index + match.Length;
            }

            // Remaining text
            if (lastIndex < text.Length)
                inlines.Add(new Run(text.Substring(lastIndex)));

            return inlines;
        }

        private static List<Inline> BuildItalicInlines(string text, MatchCollection matches)
        {
            var inlines = new List<Inline>();
            int lastIndex = 0;

            foreach (Match match in matches)
            {
                // Text before match
                if (match.Index > lastIndex)
                    inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));

                // Italic text (without markers) - check which group matched (* or _)
                string italicText = !string.IsNullOrEmpty(match.Groups[1].Value) 
                    ? match.Groups[1].Value 
                    : match.Groups[2].Value;
                inlines.Add(new Run(italicText) { FontStyle = FontStyles.Italic });
                lastIndex = match.Index + match.Length;
            }

            // Remaining text
            if (lastIndex < text.Length)
                inlines.Add(new Run(text.Substring(lastIndex)));

            return inlines;
        }

        private static List<Inline> BuildBoldItalicInlines(string text, MatchCollection matches)
        {
            var inlines = new List<Inline>();
            int lastIndex = 0;

            foreach (Match match in matches)
            {
                // Text before match
                if (match.Index > lastIndex)
                    inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));

                // Bold italic text (without markers)
                inlines.Add(new Run(match.Groups[2].Value) 
                { 
                    FontWeight = FontWeights.Bold,
                    FontStyle = FontStyles.Italic 
                });
                lastIndex = match.Index + match.Length;
            }

            // Remaining text
            if (lastIndex < text.Length)
                inlines.Add(new Run(text.Substring(lastIndex)));

            return inlines;
        }

        private static List<Inline> BuildStrikethroughInlines(string text, MatchCollection matches)
        {
            var inlines = new List<Inline>();
            int lastIndex = 0;

            foreach (Match match in matches)
            {
                // Text before match
                if (match.Index > lastIndex)
                    inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));

                // Strikethrough text (without markers)
                inlines.Add(new Run(match.Groups[1].Value) { TextDecorations = TextDecorations.Strikethrough });
                lastIndex = match.Index + match.Length;
            }

            // Remaining text
            if (lastIndex < text.Length)
                inlines.Add(new Run(text.Substring(lastIndex)));

            return inlines;
        }
    }
}