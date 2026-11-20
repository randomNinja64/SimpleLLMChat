using System;
using System.Collections.Generic;

namespace SimpleLLMChatCLI
{
    /// <summary>
    /// Settings a turn reads from config. Built at startup and again on /reload.
    /// </summary>
    public sealed class TurnSettings
    {
        public string AssistantName;
        public List<string> EnabledTools;
        public HashSet<string> EnabledSet;
        public HashSet<string> ApprovalSet;
        public bool AutoApproval;
        public bool ShowJevConfidence;
        public ChatBlockDisplayMode ThinkingDisplay;
        public ChatBlockDisplayMode ToolCallDisplay;
        public ChatBlockDisplayMode ToolOutputDisplay;
        public int MaxConsecutiveFailures;
        public bool OutputOnly;

        public static TurnSettings FromConfig(ConfigHandler config, bool outputOnly)
        {
            string assistantName = config.Get(AppSettings.AssistantName);
            if (string.IsNullOrWhiteSpace(assistantName))
                assistantName = AppConstants.DefaultAssistantName;

            List<string> enabledTools = config.GetList(AppSettings.EnabledTools);

            return new TurnSettings
            {
                AssistantName = assistantName,
                EnabledTools = enabledTools,
                EnabledSet = new HashSet<string>(enabledTools, StringComparer.OrdinalIgnoreCase),
                ApprovalSet = new HashSet<string>(
                    config.GetList(AppSettings.ToolsRequiringApproval), StringComparer.OrdinalIgnoreCase),
                AutoApproval = string.Equals(
                    config.Get(AppSettings.ToolApprovalMode), "auto", StringComparison.OrdinalIgnoreCase),
                ShowJevConfidence = config.Get(AppSettings.ShowJevConfidence),
                ThinkingDisplay = config.Get(AppSettings.ThinkingDisplay),
                ToolCallDisplay = config.Get(AppSettings.ToolCallDisplay),
                ToolOutputDisplay = config.Get(AppSettings.ToolOutputDisplay),
                MaxConsecutiveFailures = Math.Max(0, config.Get(AppSettings.MaxConsecutiveFailedToolCalls)),
                OutputOnly = outputOnly
            };
        }
    }
}
