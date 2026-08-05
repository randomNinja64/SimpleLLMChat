/// <summary>
/// Shared default constants used by both CLI and GUI projects.
/// </summary>
public static class AppConstants
{
    public const int DefaultChatFontSize = 12;
    public const string DefaultAssistantName = "LLM";

    /// <summary>Default comma-separated knowledge-file extensions when ragAllowedExtensions is unset.</summary>
    public const string DefaultRagAllowedExtensions =
        ".md,.markdown,.txt,.rst,"
        + ".csv,.json,.xml,.yaml,.yml,.toml,.ini,"
        + ".html,.htm,.css,"
        + ".cs,.py,.js,.ts,.tsx,.jsx,.java,.go,.rs,"
        + ".c,.h,.cpp,.hpp,.rb,.php,.sql,.sh,.ps1,.bat";
}
