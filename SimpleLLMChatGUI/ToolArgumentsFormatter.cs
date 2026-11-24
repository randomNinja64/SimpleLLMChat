using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SimpleLLMChatGUI
{
    /// <summary>
    /// Turns a tool call's JSON arguments into readable text for the approval dialog:
    /// one field per line, multi-line strings (file bodies, scripts) shown with real
    /// line breaks and indented under their field name.
    /// </summary>
    public static class ToolArgumentsFormatter
    {
        private const int IndentStep = 4;

        public static string Format(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                return "(no arguments)";

            JToken root;
            try
            {
                root = JToken.Parse(arguments);
            }
            catch (JsonException)
            {
                // Not valid JSON (a model can send anything): show it as the prompt always did.
                return ToolApproval.UnescapeArguments(arguments);
            }

            JObject fields = root as JObject;
            if (fields == null)
                return root.ToString(Formatting.Indented);
            if (!fields.HasValues)
                return "(no arguments)";

            StringBuilder text = new StringBuilder();
            foreach (JProperty field in fields.Properties())
                AppendValue(text, field.Name, field.Value, 0);
            return text.ToString().TrimEnd('\r', '\n');
        }

        private static void AppendValue(StringBuilder text, string label, JToken value, int indent)
        {
            string pad = new string(' ', indent);

            JObject obj = value as JObject;
            if (obj != null)
            {
                if (!obj.HasValues)
                {
                    AppendLine(text, pad + label + ": {}");
                    return;
                }
                AppendLine(text, pad + label + ":");
                foreach (JProperty field in obj.Properties())
                    AppendValue(text, field.Name, field.Value, indent + IndentStep);
                return;
            }

            JArray array = value as JArray;
            if (array != null)
            {
                if (array.Count == 0)
                {
                    AppendLine(text, pad + label + ": []");
                    return;
                }
                AppendLine(text, pad + label + ":");
                for (int i = 0; i < array.Count; i++)
                    AppendValue(text, "[" + (i + 1) + "]", array[i], indent + IndentStep);
                return;
            }

            if (value.Type != JTokenType.String)
            {
                // Numbers, booleans and null as JSON writes them.
                AppendLine(text, pad + label + ": " + value.ToString(Formatting.None));
                return;
            }

            string s = ((string)value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            if (s.IndexOf('\n') < 0)
            {
                AppendLine(text, pad + label + ": " + s);
                return;
            }

            // A block: every line indented under the label, so the content starts
            // at the same column however long the field name is.
            AppendLine(text, pad + label + ":");
            string blockPad = new string(' ', indent + IndentStep);
            foreach (string line in s.Split('\n'))
                AppendLine(text, line.Length == 0 ? string.Empty : blockPad + line);
        }

        private static void AppendLine(StringBuilder text, string line)
        {
            text.Append(line).Append("\r\n");
        }
    }
}
