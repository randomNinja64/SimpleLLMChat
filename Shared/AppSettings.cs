using System.Collections.Generic;
using System.Globalization;

/// <summary>One LLMSettings.ini setting: its key, INI section, and default.</summary>
public abstract class Setting
{
    public readonly string Section;
    public readonly string Key;

    /// <summary>The default as written to a new LLMSettings.ini.</summary>
    public readonly string DefaultText;

    protected Setting(string section, string key, string defaultText)
    {
        Section = section;
        Key = key;
        DefaultText = defaultText;
    }

    /// <summary>Formats this setting's INI line for the given value.</summary>
    public string Line(object value)
    {
        return Key + "=" + value;
    }
}

public sealed class Setting<T> : Setting
{
    /// <summary>The value read when the key is missing (or unparsable).</summary>
    public readonly T Default;

    public Setting(string section, string key, T defaultValue, string defaultText)
        : base(section, key, defaultText)
    {
        Default = defaultValue;
    }
}

/// <summary>
/// Every core setting in LLMSettings.ini, declared once. Read them with
/// <see cref="ConfigHandler.Get(Setting{string})"/> and its overloads.
/// Tool options and <c>tooltimeout.&lt;tool&gt;</c> keys are dynamic and not listed here.
/// </summary>
public static class AppSettings
{
    public const string SectionAppearance = "Appearance";
    public const string SectionRag = "RAG";
    public const string SectionSystem = "System";
    public const string SectionTools = "Tools";

    // Must be declared before the settings below: static fields initialize in order.
    private static readonly List<Setting> All = new List<Setting>();

    public static readonly Setting<string> AssistantName = Def(SectionAppearance, "assistantname", AppConstants.DefaultAssistantName);
    public static readonly Setting<string> CodeBlockFontFamily = Def(SectionAppearance, "codeblockfontfamily", "");
    public static readonly Setting<string> CustomFontFamily = Def(SectionAppearance, "customfontfamily", "");
    public static readonly Setting<int> FontSize = Def(SectionAppearance, "fontsize", AppConstants.DefaultChatFontSize);
    public static readonly Setting<bool> MarkdownParsing = Def(SectionAppearance, "markdownparsing", true);
    public static readonly Setting<ChatBlockDisplayMode> ThinkingDisplay = Def(SectionAppearance, "thinkingdisplay", ChatBlockDisplayMode.Collapsed);
    public static readonly Setting<ChatBlockDisplayMode> ToolCallDisplay = Def(SectionAppearance, "toolcalldisplay", ChatBlockDisplayMode.Collapsed);
    public static readonly Setting<ChatBlockDisplayMode> ToolOutputDisplay = Def(SectionAppearance, "tooloutputdisplay", ChatBlockDisplayMode.Shown);

    public static readonly Setting<bool> RagEnabled = Def(SectionRag, "ragenabled", false);
    public static readonly Setting<string> RagAllowedExtensions = Def(SectionRag, "ragallowedextensions", AppConstants.DefaultRagAllowedExtensions);
    public static readonly Setting<int> IndexChunkOverlap = Def(SectionRag, "indexchunkoverlap", 10);
    public static readonly Setting<int> IndexChunkLines = Def(SectionRag, "indexchunklines", 60);
    public static readonly Setting<string> EmbeddingsApiKey = Def(SectionRag, "embeddingsapikey", "");
    public static readonly Setting<string> EmbeddingsEndpoint = Def(SectionRag, "embeddingsendpoint", "");
    public static readonly Setting<string> EmbeddingsModel = Def(SectionRag, "embeddingsmodel", "");
    public static readonly Setting<string> RagKnowledgePath = Def(SectionRag, "ragknowledgepath", "");
    public static readonly Setting<int> RagMaxSnippetLength = Def(SectionRag, "ragmaxsnippetlength", 2000);
    public static readonly Setting<int> RagMaxResults = Def(SectionRag, "ragmaxresults", 5);
    public static readonly Setting<string> RagRetrieveMode = Def(SectionRag, "ragretrievemode", "newchat");

    public static readonly Setting<string> ApiKey = Def(SectionSystem, "apikey", "");
    public static readonly Setting<int> ContextWindowSize = Def(SectionSystem, "contextWindowSize", 0);
    public static readonly Setting<string> LlmServer = Def(SectionSystem, "llmserver", "");
    public static readonly Setting<string> Model = Def(SectionSystem, "model", "");
    public static readonly Setting<string> SysPrompt = Def(SectionSystem, "sysprompt", "");
    public static readonly Setting<string> WorkingDirectory = Def(SectionSystem, "workingdirectory", "");

    public static readonly Setting<string> EnabledTools = Def(SectionTools, "tools", "");
    public static readonly Setting<string> ToolsRequiringApproval = Def(SectionTools, "toolsrequiringapproval", "");
    public static readonly Setting<string> AutoApprovalTools = Def(SectionTools, "autoapprovaltools", "");
    public static readonly Setting<string> JevBaseUrl = Def(SectionTools, "jevbaseurl", "");
    public static readonly Setting<string> JevApiKey = Def(SectionTools, "jevapikey", "");
    public static readonly Setting<string> JevModel = Def(SectionTools, "jevmodel", "");
    public static readonly Setting<bool> ShowJevConfidence = Def(SectionTools, "showjevconfidence", true);
    public static readonly Setting<int> MaxConsecutiveFailedToolCalls = Def(SectionTools, "maxconsecutivefailedtoolcalls", 10);

    /// <summary>The <c>key=default</c> lines of one section, for a new LLMSettings.ini.</summary>
    public static List<string> DefaultLines(string section)
    {
        var lines = new List<string>();
        foreach (Setting setting in All)
        {
            if (setting.Section == section)
                lines.Add(setting.Line(setting.DefaultText));
        }
        return lines;
    }

    private static Setting<T> Def<T>(string section, string key, T defaultValue)
    {
        var setting = new Setting<T>(section, key, defaultValue, Format(defaultValue));
        All.Add(setting);
        return setting;
    }

    private static string Format(object value)
    {
        if (value is bool)
            return (bool)value ? "1" : "0";
        if (value is ChatBlockDisplayMode)
            return value.ToString().ToLowerInvariant();
        return System.Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
