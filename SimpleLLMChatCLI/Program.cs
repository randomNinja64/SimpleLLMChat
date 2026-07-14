using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SimpleLLMChatCLI
{
    internal class Program
    {
        public static ConfigHandler Config;
        public static StatusPipeServer StatusPipe;

        // Print interactive CLI instructions
        private static readonly string[] ValidReasoningEfforts =
        {
            "none", "minimal", "low", "medium", "high", "xhigh"
        };

        static void PrintCliInstructions()
        {
            Console.WriteLine("=== SimpleLLMChat CLI ===");
            Console.WriteLine("Type '/exit' to quit.");
            Console.WriteLine("Type '/clear' to reset the chat.");
            Console.WriteLine("Type '/image \"path\" prompt' to send an image.");
            Console.WriteLine("Type '/reasoning <effort>' to set reasoning effort; '/reasoning' alone uses the API default.");
        }

        static bool TryParseImageCommand(string input, out string imagePath, out string textPrompt)
        {
            imagePath = null;
            textPrompt = null;

            if (input.Length < 8 || !input.StartsWith("/image "))
                return false;

            int quoteStart = input.IndexOf('"', 7);
            int quoteEnd = input.IndexOf('"', quoteStart + 1);

            if (quoteStart == -1 || quoteEnd == -1)
                return false;

            imagePath = input.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
            textPrompt = (quoteEnd + 1 < input.Length)
                ? input.Substring(quoteEnd + 1).TrimStart(' ', '\t')
                : string.Empty;
            return true;
        }

        static bool IsValidReasoningEffort(string effort)
        {
            foreach (string valid in ValidReasoningEfforts)
            {
                if (string.Equals(valid, effort, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static void Main(string[] args)
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;

            // Load configuration
            Config = new ConfigHandler(System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "LLMSettings.ini"));

            // Always-on status pipe for GUI (and optional external monitors)
            StatusPipe = new StatusPipeServer();
            StatusPipe.Start();

            try
            {
                Run(args);
            }
            finally
            {
                if (StatusPipe != null)
                {
                    StatusPipe.Dispose();
                    StatusPipe = null;
                }
            }
        }

        static void Run(string[] args)
        {
            // Initialize tool registry and load tools from tools/ directory
            ToolRegistry registry = new ToolRegistry(Config);
            string toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
            registry.LoadToolsFromDirectory(toolsDir);

            // Initialize LLMClient
            LLMClient client = new LLMClient(Config, registry);

            // Get enabled tools
            List<string> enabledTools = Config.GetConfigList("tools");

            // Get tools requiring approval
            List<string> toolsRequiringApproval = Config.GetConfigList("toolsrequiringapproval");

            // Get show tool output setting
            bool showToolOutput = Config.GetConfigBool("showtooloutput");

            // Conversation storage
            List<LLMClient.ChatMessage> conversation = new List<LLMClient.ChatMessage>();

            // Token status starts at 0; base overhead is measured on the first real request.
            client.PublishStatusTokens(0);

            bool showBanners = true;

            // Check if any command-line arguments were provided
            if (args.Length > 0)
            {
                // List to hold parts of the text prompt
                List<string> promptParts = new List<string>();
                // Flag to indicate if only output should be shown
                bool outputOnly = false;
                // String to store image
                string base64Image = null;

                // Loop over each command-line argument
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];

                    if (arg == "--no-banners")
                    {
                        showBanners = false;
                        continue;
                    }

                    // Check for output-only flag
                    if (arg == "-o" || arg == "--output-only")
                    {
                        outputOnly = true;
                        continue;
                    }

                    if (arg == "--reasoning-effort")
                    {
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("Error: --reasoning-effort flag requires a value.");
                            return;
                        }

                        string effort = args[++i];
                        if (!IsValidReasoningEffort(effort))
                        {
                            Console.Error.WriteLine("Error: Invalid reasoning effort. Valid values: none, minimal, low, medium, high, xhigh.");
                            return;
                        }

                        client.ReasoningEffort = effort;
                        continue;
                    }

                    // Check for image flag
                    if (arg == "--image")
                    {
                        // Ensure there is a next argument for the image path
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("Error: --image flag requires a file path.");
                            return;
                        }

                        string imagePath = args[++i];

                        try
                        {
                            base64Image = ImageEncoder.ImageFileToBase64(imagePath);
                        }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine("Error processing image: " + e.Message);
                            return;
                        }
                        continue;
                    }

                    // If not a flag, treat argument as part of the text prompt
                    promptParts.Add(arg);
                }

                // Combine all prompt parts into a single string separated by spaces
                string textPrompt = string.Join(" ", promptParts.ToArray());

                bool runNonInteractive = promptParts.Count > 0 || base64Image != null || outputOnly;
                if (runNonInteractive)
                {
                    client.ProcessConversation(
                        conversation,
                        textPrompt,
                        base64Image,
                        Config.GetConfigValue("assistantname"),
                        enabledTools,
                        toolsRequiringApproval,
                        outputOnly,
                        showToolOutput);
                    return;
                }
            }

            if (showBanners)
            {
                PrintCliInstructions();
            }

            while (true)
            {
                Console.Write("You: ");
                string userInput = Console.ReadLine();

                if (userInput == null)
                    continue;

                userInput = userInput.Replace("<<NEWLINE>>", "\n");

                if (userInput == "/exit")
                    break;

                if (userInput == "/clear")
                {
                    conversation.Clear();
                    client.PublishStatusTokens(client.GetBaseCharacterOverhead());
                    if (showBanners)
                    {
                        Console.WriteLine("Context cleared.\n");
                        PrintCliInstructions();
                    }
                    continue;
                }

                if (userInput == "/reasoning" || userInput.StartsWith("/reasoning "))
                {
                    string effort = userInput.Length > 11 ? userInput.Substring(11).Trim() : string.Empty;
                    if (!string.IsNullOrEmpty(effort) && !IsValidReasoningEffort(effort))
                    {
                        Console.Error.WriteLine("Error: Invalid reasoning effort. Valid values: none, minimal, low, medium, high, xhigh.");
                        continue;
                    }

                    client.ReasoningEffort = string.IsNullOrEmpty(effort) ? null : effort;
                    continue;
                }

                string textPrompt = null;
                string imageBase64 = null;

                string imagePath;
                if (TryParseImageCommand(userInput, out imagePath, out textPrompt))
                {
                    try
                    {
                        imageBase64 = ImageEncoder.ImageFileToBase64(imagePath);
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine("Error processing image: " + e.Message);
                        continue;
                    }
                }
                else
                {
                    // Regular text message
                    textPrompt = userInput;
                }

                client.ProcessConversation(conversation,
                                textPrompt,
                                imageBase64,
                                Config.GetConfigValue("assistantname"),
                                enabledTools,
                                toolsRequiringApproval,
                                false,
                                showToolOutput);

                Console.WriteLine();
            }
        }
    }
}
