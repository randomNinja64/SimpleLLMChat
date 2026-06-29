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

        // Print interactive CLI instructions
        static void PrintCliInstructions()
        {
            Console.WriteLine("=== SimpleLLMChat CLI ===");
            Console.WriteLine("Type 'exit' to quit.");
            Console.WriteLine("Type 'clear' to reset the chat.");
            Console.WriteLine("Type 'image <filepath>' to send an image.");
        }

        static void Main(string[] args)
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;

            // Load configuration
            Config = new ConfigHandler(System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "LLMSettings.ini"));

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
                        continue; // Skip to next argument
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

                        string imagePath = args[++i]; // Get the next argument as image path

                        try
                        {
                            // Convert image file to base64 and add to list
                            base64Image = ImageEncoder.ImageFileToBase64(imagePath);
                        }
                        catch (Exception e)
                        {
                            // Handle errors reading/converting the image
                            Console.Error.WriteLine("Error processing image: " + e.Message);
                            return;
                        }
                        continue; // Skip to next argument
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

            // Main program loop
            while (true)
            {
                Console.Write("You: ");
                string userInput = Console.ReadLine();

                if (userInput == null)
                    continue;

                // Decode multi-line text: replace <<NEWLINE>> marker back to newlines
                userInput = userInput.Replace("<<NEWLINE>>", "\n");

                if (userInput == "exit")
                    break;

                if (userInput == "clear")
                {
                    conversation.Clear();

                    if (showBanners)
                    {
                        Console.WriteLine("Context cleared.\n");
                        PrintCliInstructions();
                    }
                    continue;
                }

                // String for image and prompt
                string textPrompt = null;
                string imageBase64 = null;

                // Check for "image " command
                if (userInput.Length > 6 && userInput.StartsWith("image "))
                {
                    int quoteStart = userInput.IndexOf('"', 6);
                    int quoteEnd = userInput.IndexOf('"', quoteStart + 1);

                    if (quoteStart == -1 || quoteEnd == -1)
                    {
                        Console.Error.WriteLine("Error: Please enclose the image path in quotes.");
                        continue;
                    }

                    string imagePath = userInput.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);

                    // Remaining text after the closing quote
                    textPrompt = (quoteEnd + 1 < userInput.Length)
                        ? userInput.Substring(quoteEnd + 1).TrimStart(' ', '\t')
                        : string.Empty;

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
