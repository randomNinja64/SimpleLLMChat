using Newtonsoft.Json.Linq;
using System;

/// <summary>
/// Host-side parser for the JSON-first tool stdout protocol.
/// Tools emit the envelope via ToolHelper; the CLI parses it here.
/// </summary>
public static class ToolResultParser
{
    /// <summary>
    /// Parse tool stdout JSON { "text"?, "image"? }. On failure, treats the entire stdout as plain text.
    /// </summary>
    public static void Parse(string stdout, out string text, out string imageBase64, out string imageMime)
    {
        text = stdout ?? "";
        imageBase64 = null;
        imageMime = null;

        if (string.IsNullOrWhiteSpace(stdout))
            return;

        string trimmed = stdout.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return;

        try
        {
            JToken root = JToken.Parse(trimmed);
            if (root.Type != JTokenType.Object)
                return;

            JObject obj = (JObject)root;

            JToken textToken = obj["text"];
            if (textToken != null && textToken.Type != JTokenType.Null)
                text = textToken.Type == JTokenType.String ? (textToken.Value<string>() ?? "") : textToken.ToString();
            else
                text = "";

            JObject image = obj["image"] as JObject;
            if (image != null)
            {
                JToken dataToken = image["data"];
                JToken mimeToken = image["mime"];
                string data = dataToken != null && dataToken.Type == JTokenType.String
                    ? dataToken.Value<string>()
                    : null;
                if (!string.IsNullOrEmpty(data))
                {
                    imageBase64 = data;
                    imageMime = mimeToken != null && mimeToken.Type == JTokenType.String
                        ? mimeToken.Value<string>()
                        : "image/png";
                    if (string.IsNullOrEmpty(imageMime))
                        imageMime = "image/png";
                }
            }

            if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(imageBase64))
                text = "(no text)";
        }
        catch
        {
            text = stdout ?? "";
            imageBase64 = null;
            imageMime = null;
        }
    }
}
