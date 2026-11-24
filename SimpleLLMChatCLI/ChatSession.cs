using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SimpleLLMChatCLI.RAG;

namespace SimpleLLMChatCLI
{
    /// <summary>
    /// The interactive prompt loop and its slash commands.
    /// </summary>
    internal sealed class ChatSession
    {
        private readonly LLMClient _client;
        private readonly ToolRegistry _registry;
        private readonly string _toolsDir;
        private readonly bool _showBanners;
        private readonly List<LLMClient.ChatMessage> _conversation = new List<LLMClient.ChatMessage>();
        private TurnSettings _settings;

        // A handler gets the text after the command name, or null when there is none.
        // It returns false to have the line sent to the model as a message instead.
        private readonly Dictionary<string, Func<string, bool>> _commands =
            new Dictionary<string, Func<string, bool>>(StringComparer.Ordinal);

        public ChatSession(LLMClient client, ToolRegistry registry, string toolsDir, TurnSettings settings, bool showBanners)
        {
            _client = client;
            _registry = registry;
            _toolsDir = toolsDir;
            _settings = settings;
            _showBanners = showBanners;

            _commands["/clear"] = NoArguments(Clear);
            _commands["/reload"] = NoArguments(Reload);
            _commands["/buildindex"] = NoArguments(() => { RagHost.BuildIndex(); ChatOutput.MarkSeparated(); });
            _commands["/clearindex"] = NoArguments(() => { RagHost.ClearIndex(); ChatOutput.MarkSeparated(); });
            _commands["/save"] = Save;
            _commands["/load"] = Load;
            _commands["/reasoning"] = SetReasoning;
        }

        public void Run()
        {
            if (_showBanners)
                PrintInstructions();

            while (true)
            {
                // Pad to exactly one blank line before the prompt, regardless of
                // how the previous turn's output ended.
                ChatOutput.StartBlock();
                ChatOutput.Write("You: ", false);
                Program.Pipe.PublishDiscrete(ChatPipe.ReadyLine);
                string userInput = Program.DeferredInput.Count > 0 ? Program.DeferredInput.Dequeue() : Console.ReadLine();
                ChatOutput.EndInputLine();

                // Stdin is closed (piped input ended, or the GUI went away): there is
                // nothing left to read, so exit instead of prompting forever.
                if (userInput == null)
                    break;

                userInput = userInput.Replace("<<NEWLINE>>", "\n");

                if (userInput == "/exit")
                    break;

                if (TryRunCommand(userInput))
                    continue;

                SendMessage(userInput);
            }
        }

        private bool TryRunCommand(string input)
        {
            int space = input.IndexOf(' ');
            string name = space < 0 ? input : input.Substring(0, space);

            Func<string, bool> command;
            if (!_commands.TryGetValue(name, out command))
                return false;

            return command(space < 0 ? null : input.Substring(space + 1));
        }

        private static Func<string, bool> NoArguments(Action action)
        {
            return arguments =>
            {
                if (arguments != null)
                    return false;
                action();
                return true;
            };
        }

        private void SendMessage(string userInput)
        {
            string textPrompt;
            string imagePath;
            string imageBase64 = null;
            string imageMime = null;

            if (TryParseImageCommand(userInput, out imagePath, out textPrompt))
            {
                try
                {
                    imageBase64 = ImageEncoder.ImageFileToBase64(imagePath, out imageMime);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Error processing image: " + e.Message);
                    return;
                }
            }
            else
            {
                textPrompt = userInput;
            }

            Cancel.Begin();
            _client.ProcessConversation(_conversation, textPrompt, imageBase64, imageMime, _settings);
            Cancel.End();
        }

        private void Clear()
        {
            _conversation.Clear();
            _client.PublishStatusTokens(_client.GetBaseCharacterOverhead());
            if (_showBanners)
            {
                ChatOutput.WriteLine("Context cleared.\n");
                PrintInstructions();
            }
        }

        private void Reload()
        {
            Program.Config.LoadConfig(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "LLMSettings.ini"));
            _registry.Clear();
            _registry.LoadToolsFromDirectory(_toolsDir);
            _settings = TurnSettings.FromConfig(Program.Config, false);
            RagHost.OnConfigReloaded(Program.Config);
            _client.RefreshBaseCharacterOverhead(_settings.EnabledTools);
            _client.PublishStatusTokens(
                LLMClient.GetConversationCharacterCount(_conversation) + _client.GetBaseCharacterOverhead());

            if (_showBanners)
                ChatOutput.WriteLine("Config reloaded.\n");
            else
                ChatOutput.MarkSeparated();
        }

        private bool Save(string arguments)
        {
            if (arguments == null)
                return false;

            try
            {
                ChatTranscript.Save(arguments.Trim().Trim('"'), _conversation);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error saving chat: " + ex.Message);
            }
            ChatOutput.MarkSeparated();
            return true;
        }

        private bool Load(string arguments)
        {
            if (arguments == null)
                return false;

            List<LLMClient.ChatMessage> loaded;
            try
            {
                loaded = ChatTranscript.Load(arguments.Trim().Trim('"'));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error loading chat: " + ex.Message);
                ChatOutput.MarkSeparated();
                return true;
            }

            _conversation.Clear();
            _conversation.AddRange(loaded);
            _client.PublishStatusTokens(
                LLMClient.GetConversationCharacterCount(_conversation) + _client.GetBaseCharacterOverhead());

            // No blank line before the replay: the GUI would show it at the top of the window.
            if (!_showBanners)
                ChatOutput.MarkSeparated();
            Replay();
            return true;
        }

        /// <summary>
        /// Prints a loaded conversation through the same sink a live turn uses,
        /// so it looks as it did when it ran.
        /// </summary>
        private void Replay()
        {
            StreamSink sink = null;
            foreach (LLMClient.ChatMessage message in _conversation)
            {
                if (message.Role == "user")
                {
                    EndReplayedTurn(sink);
                    sink = null;
                    ChatOutput.StartBlock();
                    ChatOutput.WriteLine("You: " + message.Content);
                    continue;
                }

                if (sink == null)
                    sink = StreamSink.For(_settings);

                if (message.Role == "tool")
                {
                    sink.ToolOutput(message.Content, ReplayedExitCode(message.Content));
                    continue;
                }

                if (!string.IsNullOrEmpty(message.Content))
                {
                    sink.ContentStart();
                    sink.Content(message.Content);
                }
                foreach (ToolRegistry.ToolCall call in message.ToolCalls ?? new List<ToolRegistry.ToolCall>())
                {
                    sink.ToolCallStarted(call.Name);
                    sink.ToolCallArgs(call.Arguments);
                }
                sink.EndResponse();
            }
            EndReplayedTurn(sink);
        }

        private static void EndReplayedTurn(StreamSink sink)
        {
            if (sink == null)
                return;
            sink.EndResponse();
            sink.EndLine();
        }

        // Tool results start with ToolRegistry.FormatCommandResult's "Exit Code: N" line.
        private static int ReplayedExitCode(string content)
        {
            Match match = Regex.Match(content ?? string.Empty, @"^Exit Code: (-?\d+)", RegexOptions.Multiline);
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private bool SetReasoning(string arguments)
        {
            string effort = (arguments ?? string.Empty).Trim();
            if (effort.Length == 0)
                _client.ReasoningEffort = null;
            else if (Program.IsValidReasoningEffort(effort))
                _client.ReasoningEffort = effort;
            else
                Console.Error.WriteLine("Error: Invalid reasoning effort. Valid values: none, minimal, low, medium, high, xhigh.");

            // Silent command (the GUI sends this programmatically): produce no
            // stdout, and don't pad the re-prompt with a separator newline.
            ChatOutput.MarkSeparated();
            return true;
        }

        private static bool TryParseImageCommand(string input, out string imagePath, out string textPrompt)
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

        private static void PrintInstructions()
        {
            ChatOutput.WriteLine("================== SimpleLLMChat ==================");
            ChatOutput.WriteLine("| Commands:                                       |");
            ChatOutput.WriteLine("| /buildindex: Reconcile knowledge index          |");
            ChatOutput.WriteLine("| /clear: Clear chat context                      |");
            ChatOutput.WriteLine("| /clearindex: Clear knowledge index              |");
            ChatOutput.WriteLine("| /exit: Exit the application                     |");
            ChatOutput.WriteLine("| /image \"path\" prompt: Send an image             |");
            ChatOutput.WriteLine("| /load <path>: Load a saved chat                 |");
            ChatOutput.WriteLine("| /reasoning [effort]: Set reasoning effort       |");
            ChatOutput.WriteLine("| /reload: Reload configuration                   |");
            ChatOutput.WriteLine("| /save <path>: Save chat as JSON                 |");
            ChatOutput.WriteLine("| Ctrl+C: Stop the response in progress           |");
            ChatOutput.WriteLine("===================================================");
        }
    }
}
