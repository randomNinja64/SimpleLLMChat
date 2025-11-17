using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;

public class LLMClient
{
    private struct PropertyInfo
    {
        public string Type;
        public string Description;

        public PropertyInfo(string type, string description)
        {
            Type = type;
            Description = description;
        }
    }
    
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

        // Enable modern TLS protocols for HTTPS support
        // Using numeric values for compatibility with .NET 3.5
        // Tls = 192, Tls11 = 768, Tls12 = 3072
        // We use |= to ADD to existing protocols rather than replacing them
        // This ensures fallback to older protocols if newer ones aren't available
        try
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)192 | (SecurityProtocolType)768 | (SecurityProtocolType)3072;
        }
        catch
        {
            // If setting TLS protocols fails, continue with system defaults
            // This can happen on very old systems without TLS 1.2 support
        }
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
        bool outputOnly,
        bool showToolOutput)
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
                        Console.WriteLine("\n[tool request] " + call.Name + " with arguments: " + call.Arguments);
                    }

                    int exitCode = 0;

                    string toolContent;

                    bool handled;
                    if (!enabledTools.Contains(call.Name))
                    {
                        toolContent = "error: tool '" + call.Name + "' is disabled by configuration.";
                        handled = true;
                    }
                    else
                    {
                        // Execute the requested tool and capture its output
                        handled = ToolHandler.ExecuteToolCall(call, out toolContent, out exitCode);
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
                        if (showToolOutput)
                        {
                            Console.WriteLine("[tool output]");
                            Console.Write(toolContent);
                        }
                        else
                        {
                            // Show only the exit code
                            Console.WriteLine("[tool output]");
                            Console.WriteLine("Exit Code: " + exitCode);
                        }
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

    private JObject CreateToolDefinition(string name, string description, Dictionary<string, PropertyInfo> properties, string[] required)
    {
        JObject tool = new JObject
        {
            ["type"] = "function"
        };

        JObject func = new JObject
        {
            ["name"] = name,
            ["description"] = description
        };

        JObject parameters = new JObject
        {
            ["type"] = "object"
        };

        JObject props = new JObject();
        foreach (var prop in properties)
        {
            JObject propObj = new JObject
            {
                ["type"] = prop.Value.Type,
                ["description"] = prop.Value.Description
            };
            props[prop.Key] = propObj;
        }
        parameters["properties"] = props;
        parameters["required"] = new JArray(required);

        func["parameters"] = parameters;
        tool["function"] = func;
        return tool;
    }

    private JArray BuildToolsArray(List<string> enabledTools)
    {
        JArray toolsArray = new JArray();

        foreach (var toolName in enabledTools)
        {
            JObject tool = null;

            switch (toolName)
            {
                case "run_shell_command":
                    tool = CreateToolDefinition(
                        "run_shell_command",
                        "Execute a shell command on the host system and return its output.",
                        new Dictionary<string, PropertyInfo>
                        {
                            { "command", new PropertyInfo("string", "Full command line to execute. Keep it short and avoid interactive programs.") }
                        },
                        new[] { "command" }
                    );
                    break;

                case "run_web_search":
                    tool = CreateToolDefinition(
                        "run_web_search",
                        "Search the web using DuckDuckGo and return a list of URLs with brief snippets. If more detail is needed, URLs can be read with read_website.",
                        new Dictionary<string, PropertyInfo>
                        {
                            { "query", new PropertyInfo("string", "The search query to look up on the web.") }
                        },
                        new[] { "query" }
                    );
                    break;

                case "read_website":
                    tool = CreateToolDefinition(
                        "read_website",
                        "Browse to a specific URL/web page and return its HTML content.",
                        new Dictionary<string, PropertyInfo>
                        {
                            { "URL", new PropertyInfo("string", "The URL of the web page to get the content of.") }
                        },
                        new[] { "URL" }
                    );
                    break;

                case "download_video":
                    tool = CreateToolDefinition(
                        "download_video",
                        "Download an online video using YT-DLP to the user's desktop, returning YT-DLP's output",
                        new Dictionary<string, PropertyInfo>
                        {
                            { "URL", new PropertyInfo("string", "The URL of the video to download.") }
                        },
                        new[] { "URL" }
                    );
                    break;

                case "download_file":
                    tool = CreateToolDefinition(
                        "download_file",
                        "Downloads a file from the internet using cURL and saves it to the provided location.",
                        new Dictionary<string, PropertyInfo>
                        {
                            { "filename", new PropertyInfo("string", "The full path of the file to write to. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                            { "URL", new PropertyInfo("string", "The URL of the file to download.") }
                        },
                        new[] { "filename", "URL" }
                    );
                    break;

                case "read_file":
                    tool = CreateToolDefinition(
                        "read_file",
                        $"Read the contents of a local file and return it as a string. Always reads up to {FileHandler.MAX_CONTENT_LENGTH} characters. Use the offset parameter to read different parts of large files.",
                        new Dictionary<string, PropertyInfo>
                        {
                            { "filename", new PropertyInfo("string", "The full path of the file to read. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                            { "offset", new PropertyInfo("string", $"Optional. Character offset to start reading from (default: 0). Use this to read different parts of large files. For example, offset {FileHandler.MAX_CONTENT_LENGTH} reads characters {FileHandler.MAX_CONTENT_LENGTH}-{FileHandler.MAX_CONTENT_LENGTH * 2}.") }
                        },
                        new[] { "filename" }
                    );
                    break;

                case "write_file":
                    tool = CreateToolDefinition(
                        "write_file",
                        "Write the given content to a local file, creating or overwriting it.",
                        new Dictionary<string, PropertyInfo>
                        {
                            { "filename", new PropertyInfo("string", "The full path of the file to write to. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                            { "content", new PropertyInfo("string", "The content to write into the file.") }
                        },
                        new[] { "filename", "content" }
                    );
                    break;

                case "extract_file":
                    tool = CreateToolDefinition(
                        "extract_file",
                        "Extract an archive file using 7za.exe to a specified destination directory.",
                        new Dictionary<string, PropertyInfo>
                        {
                            { "archive_path", new PropertyInfo("string", "The full path of the archive file to extract. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                            { "destination_path", new PropertyInfo("string", "The full path of the destination directory where files will be extracted. Directory will be created if it doesn't exist. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                        },
                        new[] { "archive_path", "destination_path" }
                    );
                    break;

                case "move_file":
                    tool = CreateToolDefinition(
                        "move_file",
                        "Move or rename a file from one location to another. Destination directory will be created if it doesn't exist.",
                        new Dictionary<string, PropertyInfo>
                        {
                            { "source_path", new PropertyInfo("string", "The full path of the file to move. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                            { "destination_path", new PropertyInfo("string", "The full path where the file should be moved to. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                        },
                        new[] { "source_path", "destination_path" }
                    );
                    break;

                case "copy_file":
                    tool = CreateToolDefinition(
                        "copy_file",
                        "Copy a file from one location to another. Destination directory will be created if it doesn't exist.",
                        new Dictionary<string, PropertyInfo>
                        {
                            { "source_path", new PropertyInfo("string", "The full path of the file to copy. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                            { "destination_path", new PropertyInfo("string", "The full path where the file should be copied to. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                        },
                        new[] { "source_path", "destination_path" }
                    );
                    break;

                case "delete_file":
                    tool = CreateToolDefinition(
                        "delete_file",
                        "Delete a file from the file system. Use with caution as this operation cannot be undone.",
                        new Dictionary<string, PropertyInfo>
                        {
                            { "file_path", new PropertyInfo("string", "The full path of the file to delete. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                        },
                        new[] { "file_path" }
                    );
                    break;

                case "list_directory":
                    tool = CreateToolDefinition(
                        "list_directory",
                        "List all files and subdirectories in a given directory.",
                        new Dictionary<string, PropertyInfo>
                        {
                            { "directory_path", new PropertyInfo("string", "The full path of the directory to list. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                        },
                        new[] { "directory_path" }
                    );
                    break;
            }

            if (tool != null)
            {
                toolsArray.Add(tool);
            }
        }

        return toolsArray;
    }

    LLMCompletionResponse sendMessages(List<ChatMessage> conversation, List<string> enabledTools)
    {
        LLMCompletionResponse completionResponse = new LLMCompletionResponse
        {
            Content = string.Empty,
            ToolCalls = new List<ToolHandler.ToolCall>(),
            FinishReason = string.Empty
        };

        // Build payload
        JObject payload = new JObject
        {
            ["model"] = model
        };

        // Messages
        JArray messages = new JArray();

        // System message
        JObject systemMsg = new JObject
        {
            ["role"] = "system",
            ["content"] = systemPrompt
        };
        messages.Add(systemMsg);

        // Process all user messages in the conversation list
        if (conversation != null)
        {
            foreach (var msg in conversation)
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

                messages.Add(msgObj);
            }
        }

        payload["messages"] = messages;

        // Add tools if any are enabled
        if (enabledTools != null && enabledTools.Count > 0)
        {
            JArray toolsArray = BuildToolsArray(enabledTools);
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

                                        var temp = partialToolCalls[index];

                                        if (!string.IsNullOrEmpty(id))
                                        {
                                            temp.Id = id;
                                        }

                                        if (function != null)
                                        {
                                            string name = (string)function["name"];
                                            string argsChunk = (string)function["arguments"];

                                            if (!string.IsNullOrEmpty(name))
                                            {
                                                temp.Name = name;
                                            }

                                            if (!string.IsNullOrEmpty(argsChunk))
                                            {
                                                temp.Arguments += argsChunk;
                                            }
                                        }

                                        partialToolCalls[index] = temp;
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
