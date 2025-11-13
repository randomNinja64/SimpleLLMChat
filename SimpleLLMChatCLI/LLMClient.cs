using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using static System.Net.WebRequestMethods;

public class LLMClient
{
    private readonly string llmEndpoint;
    private readonly string apiKey;
    private readonly string model;
    private readonly string systemPrompt;

    public LLMClient(string llmEndpoint, string key, string mdl, string sysprompt)
    {
        this.llmEndpoint = llmEndpoint;
        this.apiKey = key;
        this.model = mdl;
        this.systemPrompt = sysprompt;
    }

    // Struct for chat messages
    public struct ChatMessage
    {
        public string Role;
        public string Content;
        public string Image;
        public List<ToolHandler.ToolCall> ToolCalls;
        public string ToolCallId;

        public ChatMessage(string role, string content, string toolCallId = "")
        {
            Role = role;
            Content = content;
            ToolCallId = toolCallId;
            Image = null;
            ToolCalls = new List<ToolHandler.ToolCall>();
        }
    }

    public struct LLMCompletionResponse
    {
        public string Content;
        public List<ToolHandler.ToolCall> ToolCalls;
        public string FinishReason;

        public LLMCompletionResponse(string content, List<ToolHandler.ToolCall> toolCalls, string finishReason)
        {
            Content = content;
            ToolCalls = toolCalls ?? new List<ToolHandler.ToolCall>();
            FinishReason = finishReason;
        }
    }

    public void ProcessConversation(
        List<ChatMessage> conversation,
        string userMessage,
        string image,
        string assistantName,
        List<string> enabledTools,
        bool outputOnly)
    {
        // Add user message
        ChatMessage userMsg = new ChatMessage
        {
            Role = "user",
            Content = userMessage,
            Image = image
        };
        conversation.Add(userMsg);

        while (true)
        {
            if (!outputOnly)
            {
                Console.WriteLine();
                Console.Write(assistantName + ": ");
            }

            LLMCompletionResponse response = sendMessages(conversation, enabledTools);

            if (response.ToolCalls != null && response.ToolCalls.Count > 0)
            {
                // Add assistant tool call message
                ChatMessage assistantCall = new ChatMessage
                {
                    Role = "assistant",
                    Content = string.Empty,
                    ToolCalls = response.ToolCalls
                };
                conversation.Add(assistantCall);

                for (int i = 0; i < response.ToolCalls.Count; i++)
                {
                    ToolHandler.ToolCall call = response.ToolCalls[i];

                    if (!outputOnly)
                    {
                        Console.WriteLine("\n[tool request] " + call.Name);
                    }

                    bool handled = false;

                    string toolContent;
                    if (!enabledTools.Contains(call.Name))
                    {
                        toolContent = "error: tool '" + call.Name + "' is disabled by configuration.";
                        handled = true;
                    }
                    else
                    {
                        // Execute the requested tool and capture its output
                        handled = ToolHandler.ExecuteToolCall(call, out toolContent);
                    }

                    ChatMessage toolMsg = new ChatMessage
                    {
                        Role = "tool",
                        Content = toolContent,
                        ToolCallId = call.Id
                    };
                    conversation.Add(toolMsg);

                    if (!outputOnly)
                    {
                        Console.WriteLine("[tool output]");
                        Console.Write(toolContent);
                    }

                    if (!handled && !outputOnly)
                    {
                        Console.WriteLine("[warning] tool not fully handled.");
                    }
                }

                // Run loop again so assistant can ingest tool output
                continue;
            }

            // Add assistant message
            ChatMessage assistantMsg = new ChatMessage
            {
                Role = "assistant",
                Content = response.Content
            };
            conversation.Add(assistantMsg);
            break;
        }
    }

    LLMCompletionResponse sendMessages(List<ChatMessage> conversation, List<string> enabledTools)
    {
        // See if we're up!
        try
        {
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(llmEndpoint);
            request.Method = "HEAD"; // lightweight request just to test connection
            request.Timeout = 5000; // 5 seconds timeout
            using (var headResponse = (System.Net.HttpWebResponse)request.GetResponse())
            {
                if (headResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    Console.Error.WriteLine("Connection failed: " + headResponse.StatusCode);
                    return new LLMCompletionResponse("", null, "connection_failed");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Connection failed: " + ex.Message);
            return new LLMCompletionResponse("", null, "connection_failed");
        }

        LLMCompletionResponse completionResponse = new LLMCompletionResponse
        {
            Content = string.Empty,
            ToolCalls = new List<ToolHandler.ToolCall>(),
            FinishReason = string.Empty
        };

        // Build payload
        JObject payload = new JObject();
        payload["model"] = model;

        // Messages
        JArray messages = new JArray();

        // System message
        JObject systemMsg = new JObject();
        systemMsg["role"] = "system";
        systemMsg["content"] = systemPrompt;
        messages.Add(systemMsg);

        // Process all user messages in the conversation list
        if (conversation != null)
        {
            foreach (var msg in conversation)
            {
                JObject msgObj = new JObject();
                msgObj["role"] = msg.Role;

                if (!string.IsNullOrEmpty(msg.ToolCallId))
                    msgObj["tool_call_id"] = msg.ToolCallId;

                if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    msgObj["content"] = msg.Content ?? "";
                    JArray toolCallsArray = new JArray();

                    foreach (var call in msg.ToolCalls)
                    {
                        JObject toolObj = new JObject();
                        toolObj["id"] = call.Id ?? "";
                        toolObj["type"] = "function";

                        JObject functionObj = new JObject();
                        functionObj["name"] = call.Name ?? "";
                        functionObj["arguments"] = call.Arguments ?? "";

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
                        JObject textPart = new JObject();
                        textPart["type"] = "text";
                        textPart["text"] = msg.Content;
                        contentArray.Add(textPart);
                    }

                    if (!string.IsNullOrEmpty(msg.Image))
                    {
                        JObject imgPart = new JObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JObject
                            {
                                ["url"] = "data:image/png;base64," + msg.Image
                            }
                        };
                        contentArray.Add(imgPart);
                    }

                    if (contentArray.Count == 0)
                    {
                        JObject emptyText = new JObject();
                        emptyText["type"] = "text";
                        emptyText["text"] = "";
                        contentArray.Add(emptyText);
                    }

                    msgObj["content"] = contentArray;
                }
                else
                {
                    msgObj["content"] = msg.Content ?? "";
                }

                messages.Add(msgObj);
            }
        }

        payload["messages"] = messages;

        // Tools
        if (enabledTools != null && enabledTools.Count > 0)
        {
            JArray toolsArray = new JArray();
            foreach (var toolName in enabledTools)
            {
                if (toolName == "run_shell_command")
                {
                    JObject tool = new JObject();
                    tool["type"] = "function";
                    JObject func = new JObject();
                    func["name"] = "run_shell_command";
                    func["description"] = "Execute a shell command on the host system and return its output.";
                    JObject parameters = new JObject();
                    parameters["type"] = "object";
                    JObject properties = new JObject();
                    JObject commandProp = new JObject();
                    commandProp["type"] = "string";
                    commandProp["description"] = "Full command line to execute. Keep it short and avoid interactive programs.";
                    properties["command"] = commandProp;
                    parameters["properties"] = properties;
                    parameters["required"] = new JArray("command");
                    func["parameters"] = parameters;
                    tool["function"] = func;
                    toolsArray.Add(tool);
                }
                else if (toolName == "run_web_search")
                {
                    JObject tool = new JObject();
                    tool["type"] = "function";
                    JObject func = new JObject();
                    func["name"] = "run_web_search";
                    func["description"] = "Search the web using DuckDuckGo and return HTML results.";
                    JObject parameters = new JObject();
                    parameters["type"] = "object";
                    JObject properties = new JObject();
                    JObject queryProp = new JObject();
                    queryProp["type"] = "string";
                    queryProp["description"] = "The search query to look up on the web.";
                    properties["query"] = queryProp;
                    parameters["properties"] = properties;
                    parameters["required"] = new JArray("query");
                    func["parameters"] = parameters;
                    tool["function"] = func;
                    toolsArray.Add(tool);
                }
            }

            if (toolsArray.Count > 0)
                payload["tools"] = toolsArray;
        }

        payload["stream"] = true;

        // Send HTTP request
        try
        {
            var request = (HttpWebRequest)WebRequest.Create($"{llmEndpoint}/v1/chat/completions");
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", "Bearer " + apiKey);

            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            request.ContentLength = payloadBytes.Length;

            using (var stream = request.GetRequestStream())
            {
                stream.Write(payloadBytes, 0, payloadBytes.Length);
            }

            using (var httpResponse = (HttpWebResponse)request.GetResponse())
            using (var responseStream = httpResponse.GetResponseStream())
            using (var reader = new StreamReader(responseStream, Encoding.UTF8))
            {
                string line;
                StringBuilder output = new StringBuilder();

                // ✅ accumulate tool call argument chunks across deltas
                Dictionary<int, ToolHandler.ToolCall> partialToolCalls = new Dictionary<int, ToolHandler.ToolCall>();

                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("data: "))
                    {
                        string jsonPart = line.Substring(6);
                        if (jsonPart == "[DONE]") break;

                        try
                        {
                            JObject obj = JObject.Parse(jsonPart);
                            JArray choices = (JArray)obj["choices"];
                            if (choices == null) continue;

                            foreach (JObject choice in choices)
                            {
                                string content = (string)choice["delta"]?["content"];
                                if (!string.IsNullOrEmpty(content))
                                {
                                    Console.Write(content);
                                    output.Append(content);
                                }

                                string finishReason = (string)choice["finish_reason"];
                                if (!string.IsNullOrEmpty(finishReason))
                                    completionResponse.FinishReason = finishReason;

                                JArray toolCalls = (JArray)choice["delta"]?["tool_calls"];
                                if (toolCalls != null)
                                {
                                    foreach (JObject call in toolCalls)
                                    {
                                        int index = call["index"]?.Value<int>() ?? 0;
                                        string id = (string)call["id"];
                                        JObject function = (JObject)call["function"];

                                        if (!partialToolCalls.ContainsKey(index))
                                        {
                                            partialToolCalls[index] = new ToolHandler.ToolCall
                                            {
                                                Id = "",
                                                Name = "",
                                                Arguments = ""
                                            };
                                        }

                                        if (!string.IsNullOrEmpty(id))
                                        {
                                            var temp = partialToolCalls[index];
                                            temp.Id = id;
                                            partialToolCalls[index] = temp;
                                        }

                                        if (function != null)
                                        {
                                            string name = (string)function["name"];
                                            string argsChunk = (string)function["arguments"];

                                            if (!string.IsNullOrEmpty(name))
                                            {
                                                var temp = partialToolCalls[index];
                                                temp.Name = name;
                                                partialToolCalls[index] = temp;
                                            }

                                            if (!string.IsNullOrEmpty(argsChunk))
                                            {
                                                var temp = partialToolCalls[index];
                                                temp.Arguments += argsChunk;
                                                partialToolCalls[index] = temp;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // ignore malformed JSON fragments
                        }
                    }
                }

                // finalize tool calls after stream ends
                completionResponse.ToolCalls.AddRange(partialToolCalls.Values);
                completionResponse.Content = output.ToString();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error sending request: " + ex.Message);

            return new LLMCompletionResponse("", null, "request_failed");
        }

        return completionResponse;
    }
}
