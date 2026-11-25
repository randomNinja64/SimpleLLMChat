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

    // Context injector output, taken once per chat. A live snapshot on every turn
    // would change the system prompt each time and invalidate the provider's cached
    // prompt prefix; tools like list_windows give the model the current state instead.
    private List<string> contextSnapshot;
    private string contextSnapshotKey;

    /// <summary>
    /// Drops the context injector snapshot so the next request takes a fresh one.
    /// Called when a chat starts, is loaded, or is replaced by a summary.
    /// </summary>
    public void ResetContextInjections()
    {
        contextSnapshot = null;
        contextSnapshotKey = null;
    }

    private List<string> GetContextInjections(List<string> enabledTools)
    {
        if (registry == null || enabledTools == null || enabledTools.Count == 0)
            return new List<string>();

        // A different tool set changes the tools array (and the cache) anyway.
        string key = string.Join("\n", enabledTools.ToArray());
        if (contextSnapshot == null || contextSnapshotKey != key)
        {
            contextSnapshot = registry.GetContextInjections(enabledTools);
            contextSnapshotKey = key;
        }
        return contextSnapshot;
    }

    public void RefreshBaseCharacterOverhead(List<string> enabledTools)
    {
        string systemPrompt = BuildSystemPrompt(enabledTools);
        int toolsChars = 0;
        if (enabledTools != null && enabledTools.Count > 0 && registry != null)
        {
            JArray toolsArray = registry.BuildToolsArray(enabledTools);
            if (toolsArray.Count > 0)
                toolsChars = toolsArray.ToString(Formatting.None).Length;
        }
        BaseOverheadChars = systemPrompt.Length + toolsChars;
    }

    private string BuildSystemPrompt(List<string> enabledTools)
    {
        string sysprompt = ConfigHandler.DecodeStoredPrompt(config.Get(AppSettings.SysPrompt)) ?? "";

        foreach (string injection in GetContextInjections(enabledTools))
            sysprompt += "\n\n" + injection;

        string ragHint = RagHost.GetKnowledgePathHint(config);
        if (!string.IsNullOrEmpty(ragHint))
        {
            if (sysprompt.Length > 0)
                sysprompt += "\n\n";
            sysprompt += ragHint;
        }

        return sysprompt;
    }

    private JObject BuildRequestPayload(List<ChatMessage> conversation, List<string> enabledTools)
    {
        JObject payload = new JObject
        {
            ["model"] = config.Get(AppSettings.Model)
        };

        string systemPrompt = BuildSystemPrompt(enabledTools);

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
            List<string> configuredTools = config.GetList(AppSettings.EnabledTools);
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
        // What the model reads: retrieved context (if any) ahead of the user's words.
        string content = string.IsNullOrEmpty(msg.Context)
            ? msg.Content
            : AutoRagContext.MergeIntoPrompt(msg.Context, msg.Content);

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

            if (!string.IsNullOrEmpty(content))
            {
                JObject textPart = new JObject
                {
                    ["type"] = "text",
                    ["text"] = content
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
            msgObj["content"] = content ?? "";
        }

        return msgObj;
    }
}
}
