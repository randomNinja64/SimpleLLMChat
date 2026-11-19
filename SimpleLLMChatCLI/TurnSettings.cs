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
            string assistantName = config.GetConfigValue("assistantname");
            if (string.IsNullOrWhiteSpace(assistantName))
                assistantName = AppConstants.DefaultAssistantName;

            List<string> enabledTools = config.GetConfigList("tools");

            return new TurnSettings
            {
                AssistantName = assistantName,
                EnabledTools = enabledTools,
                EnabledSet = new HashSet<string>(enabledTools, StringComparer.OrdinalIgnoreCase),
                ApprovalSet = new HashSet<string>(
                    config.GetConfigList("toolsrequiringapproval"), StringComparer.OrdinalIgnoreCase),
                AutoApproval = string.Equals(
                    config.GetConfigValue("toolapprovalmode"), "auto", StringComparison.OrdinalIgnoreCase),
                ShowJevConfidence = config.GetConfigBool("showjevconfidence", true),
                ThinkingDisplay = config.GetChatBlockDisplayMode("thinkingdisplay", ChatBlockDisplayMode.Collapsed),
                ToolCallDisplay = config.GetChatBlockDisplayMode("toolcalldisplay", ChatBlockDisplayMode.Collapsed),
                ToolOutputDisplay = config.GetChatBlockDisplayMode("tooloutputdisplay", ChatBlockDisplayMode.Shown),
                MaxConsecutiveFailures = Math.Max(0, config.GetConfigInt("maxconsecutivefailedtoolcalls", 10)),
                OutputOnly = outputOnly
            };
        }
    }
}
