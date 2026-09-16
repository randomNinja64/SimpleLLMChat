using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SimpleLLMChatCLI.RAG;
using System.Collections.Generic;

namespace SimpleLLMChatCLI
{
public partial class LLMClient
{
    public int GetBaseCharacterOverhead()
    {
        return BaseOverheadChars.HasValue ? BaseOverheadChars.Value : 0;
    }

    public void RefreshBaseCharacterOverhead(List<string> enabledTools)
    {
        List<string> contextInjections = registry != null
            ? registry.GetContextInjections(enabledTools)
            : new List<string>();
        string systemPrompt = BuildSystemPrompt(enabledTools, contextInjections);
        int toolsChars = 0;
        if (enabledTools != null && enabledTools.Count > 0 && registry != null)
        {
            JArray toolsArray = registry.BuildToolsArray(enabledTools);
            if (toolsArray.Count > 0)
                toolsChars = toolsArray.ToString(Formatting.None).Length;
        }
        BaseOverheadChars = systemPrompt.Length + toolsChars;
    }

    private string BuildSystemPrompt(List<string> enabledTools, List<string> contextInjections = null)
    {
        string sysprompt = ConfigHandler.DecodeStoredPrompt(config.GetConfigValue("sysprompt")) ?? "";

        if (registry != null)
        {
            List<string> injections = contextInjections ?? registry.GetContextInjections(enabledTools);
            foreach (string injection in injections)
                sysprompt += "\n\n" + injection;
        }

        string ragHint = RagHost.GetKnowledgePathHint(config);
        if (!string.IsNullOrEmpty(ragHint))
        {
            if (sysprompt.Length > 0)
                sysprompt += "\n\n";
            sysprompt += ragHint;
        }

        return sysprompt;
    }

    private JObject BuildRequestPayload(List<ChatMessage> conversation, List<string> enabledTools, List<string> contextInjections = null)
    {
        JObject payload = new JObject
        {
            ["model"] = config.GetConfigValue("model")
        };

        string systemPrompt = BuildSystemPrompt(enabledTools, contextInjections);

        JArray messages = new JArray();
        messages.Add(new JObject
        {
            ["role"] = "system",
            ["content"] = systemPrompt
        });

        if (conversation != null)
        {
            foreach (var msg in conversation)
                messages.Add(BuildMessageObject(msg));
        }

        payload["messages"] = messages;

        int toolsChars = 0;
        if (enabledTools != null && enabledTools.Count > 0 && registry != null)
        {
            JArray toolsArray = registry.BuildToolsArray(enabledTools);
            if (toolsArray.Count > 0)
            {
                payload["tools"] = toolsArray;
                toolsChars = toolsArray.ToString(Formatting.None).Length;
            }
        }

        if (!BaseOverheadChars.HasValue)
        {
            List<string> configuredTools = config.GetConfigList("tools");
            bool summarizationPass = (enabledTools == null || enabledTools.Count == 0)
                && configuredTools != null && configuredTools.Count > 0;
            if (!summarizationPass)
                BaseOverheadChars = systemPrompt.Length + toolsChars;
        }

        payload["stream"] = true;

        if (!string.IsNullOrEmpty(ReasoningEffort))
            payload["reasoning_effort"] = ReasoningEffort;

        return payload;
    }

    private JObject BuildMessageObject(ChatMessage msg)
    {
        JObject msgObj = new JObject
        {
            ["role"] = msg.Role
        };

        if (!string.IsNullOrEmpty(msg.ToolCallId))
            msgObj["tool_call_id"] = msg.ToolCallId;

        if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
        {
            msgObj["content"] = msg.Content ?? "";
            JArray toolCallsArray = new JArray();

            foreach (var call in msg.ToolCalls)
            {
                JObject toolObj = new JObject
                {
                    ["id"] = call.Id ?? "",
                    ["type"] = "function"
                };

                JObject functionObj = new JObject
                {
                    ["name"] = call.Name ?? "",
                    ["arguments"] = call.Arguments ?? ""
                };

                toolObj["function"] = functionObj;
                toolCallsArray.Add(toolObj);
            }

            msgObj["tool_calls"] = toolCallsArray;
        }
        else if (msg.Image != null)
        {
            JArray contentArray = new JArray();

            if (!string.IsNullOrEmpty(msg.Content))
            {
                JObject textPart = new JObject
                {
                    ["type"] = "text",
                    ["text"] = msg.Content
                };
                contentArray.Add(textPart);
            }

            if (!string.IsNullOrEmpty(msg.Image))
            {
                string mime = string.IsNullOrEmpty(msg.ImageMime) ? "image/png" : msg.ImageMime;
                JObject imgPart = new JObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JObject
                    {
                        ["url"] = "data:" + mime + ";base64," + msg.Image
                    }
                };
                contentArray.Add(imgPart);
            }

            if (contentArray.Count == 0)
            {
                JObject emptyText = new JObject
                {
                    ["type"] = "text",
                    ["text"] = ""
                };
                contentArray.Add(emptyText);
            }

            msgObj["content"] = contentArray;
        }
        else
        {
            msgObj["content"] = msg.Content ?? "";
        }

        return msgObj;
    }
}
}
