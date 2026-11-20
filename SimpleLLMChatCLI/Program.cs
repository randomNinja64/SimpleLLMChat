using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SimpleLLMChatCLI.RAG;

namespace SimpleLLMChatCLI
{
    internal class Program
    {
        public static ConfigHandler Config;
        public static ChatPipeServer Pipe;

        // Commands that arrived on stdin while a tool approval was waiting for Y/N.
        // The main loop runs them once the turn ends.
        public static readonly Queue<string> DeferredInput = new Queue<string>();

        private static readonly string[] ValidReasoningEfforts =
        {
            "none", "minimal", "low", "medium", "high", "xhigh"
        };

        internal static bool IsValidReasoningEffort(string effort)
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
            TlsConfig.EnsureModernProtocols();

            // Load configuration
            Config = new ConfigHandler(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "LLMSettings.ini"));

            // Always-on chat pipe for GUI (and optional external monitors)
            Pipe = new ChatPipeServer();
            Pipe.Start();
            Cancel.Listen();

            try
            {
                Run(args);
            }
            finally
            {
                if (Pipe != null)
                {
                    Pipe.Dispose();
                    Pipe = null;
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

            // Auto-RAG index check on launch when enabled
            RagHost.Initialize(Config);

            TurnSettings settings = TurnSettings.FromConfig(Config, false);

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
                string imageMime = null;

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
                            base64Image = ImageEncoder.ImageFileToBase64(imagePath, out imageMime);
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
                        new List<LLMClient.ChatMessage>(),
                        textPrompt,
                        base64Image,
                        imageMime,
                        TurnSettings.FromConfig(Config, outputOnly));
                    return;
                }
            }

            new ChatSession(client, registry, toolsDir, settings, showBanners).Run();
        }
    }
}
