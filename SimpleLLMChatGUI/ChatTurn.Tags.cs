using System;

namespace SimpleLLMChatGUI
{
    public partial class ChatTurn
    {
        private static readonly string[] CollapsibleOpenTags =
        {
            "[thinking]", "<think>", "[tool call]", "[tool output]"
        };
        private static readonly string[] CollapsibleCloseTags =
        {
            "[/thinking]", "</think>", "[/tool call]", "[/tool output]"
        };

        private static string TakeToolCallName(string text, out string name)
        {
            if (string.IsNullOrEmpty(text))
            {
                name = "tool";
                return text;
            }

            int newline = text.IndexOf('\n');
            if (newline < 0)
            {
                name = text.Trim();
                if (name.Length == 0)
                    name = "tool";
                return string.Empty;
            }

            name = text.Substring(0, newline).Trim();
            if (name.Length == 0)
                name = "tool";

            string rest = text.Substring(newline + 1);
            if (rest.Length > 0 && rest[0] == '\r')
                rest = rest.Substring(1);
            return rest;
        }

        private static bool TryFindTag(string text, string[] tags, out int index, out int length)
        {
            index = -1;
            length = 0;

            foreach (string tag in tags)
            {
                int start = 0;
                while (true)
                {
                    int found = text.IndexOf(tag, start, StringComparison.OrdinalIgnoreCase);
                    if (found < 0)
                        break;

                    if (found == 0 || text[found - 1] != '/')
                    {
                        if (index < 0 || found < index)
                        {
                            index = found;
                            length = tag.Length;
                        }
                        break;
                    }

                    start = found + 1;
                }
            }

            return index >= 0;
        }
    }
}
