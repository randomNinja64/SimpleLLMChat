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

        // Taken from the prompt as typed: the first user message in _conversation
        // can carry merged RAG context, or be replaced by a summary.
        private string _title;

        // Where this session auto-saves: a new file in its workspace's chats folder,
        // or the file /load read from (wherever it is). Null until there is one.
        // The workspace itself is _registry.ResolveWorkingDirectory().
        private string _sessionPath;

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
            _commands["/compact"] = NoArguments(Compact);
            _commands["/reload"] = NoArguments(Reload);
            _commands["/buildindex"] = NoArguments(() => { RagHost.BuildIndex(); ChatOutput.MarkSeparated(); });
            _commands["/clearindex"] = NoArguments(() => { RagHost.ClearIndex(); ChatOutput.MarkSeparated(); });
            _commands["/save"] = Save;
            _commands["/load"] = Load;
            _commands["/import"] = Import;
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
                Program.Pipe.PublishDiscrete(ChatPipe.FormatReady(_registry.ResolveWorkingDirectory()));
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

            // Image-only messages have no text, so the next one with text names the chat.
            if (_title == null)
                _title = ChatTranscript.MakeTitle(textPrompt);

            int historyLimit = Program.Config.Get(AppSettings.ChatHistoryLimit);
            if (_sessionPath == null && historyLimit > 0)
                _sessionPath = ChatHistory.NewSessionPath(_registry.ResolveWorkingDirectory());
            bool newChat = _conversation.Count == 0;
            _client.ChatRecall = new ChatRecallScope
            {
                Directory = ChatWorkspace.DirectoryFor(_registry.ResolveWorkingDirectory()),
                CurrentPath = _sessionPath
            };

            Cancel.Begin();
            _client.ProcessConversation(_conversation, textPrompt, imageBase64, imageMime, _settings);
            // Titles name saved chats, so only with history on; and not if stopped.
            if (newChat && historyLimit > 0 && !Cancel.Requested
                && Program.Config.Get(AppSettings.ChatTitlesByModel))
                TitleFromFirstReply(textPrompt);
            Cancel.End();

            // After every turn, finished or stopped.
            AutoSave(historyLimit);
        }

        /// <summary>
        /// Replaces the first-message title with one the model writes from the first
        /// exchange. Any failure keeps the first-message title.
        /// </summary>
        private void TitleFromFirstReply(string prompt)
        {
            string reply = null;
            for (int i = _conversation.Count - 1; i >= 0 && reply == null; i--)
            {
                if (_conversation[i].Role == "assistant" && !string.IsNullOrWhiteSpace(_conversation[i].Content))
                    reply = _conversation[i].Content;
            }
            if (reply == null)
                return;

            string title = ChatTranscript.CleanModelTitle(_client.GenerateTitle(prompt, reply));
            if (title != null)
                _title = title;
        }

        private void AutoSave(int historyLimit)
        {
            if (historyLimit <= 0 || _sessionPath == null || _conversation.Count == 0)
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath));
                ChatTranscript.Save(_sessionPath, _title, _registry.ResolveWorkingDirectory(), _conversation);
                ChatHistory.Prune(historyLimit, _sessionPath);
                Program.Pipe.PublishDiscrete(ChatPipe.FormatSession(_sessionPath));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error auto-saving chat: " + ex.Message);
            }
        }

        private void Clear()
        {
            _conversation.Clear();
            _title = null;
            _sessionPath = null;
            _registry.WorkingDirectoryOverride = null;
            _client.PublishStatusTokens(_client.GetBaseCharacterOverhead());
            if (_showBanners)
            {
                ChatOutput.WriteLine("Context cleared.\n");
                PrintInstructions();
            }
        }

        /// <summary>
        /// /compact: replaces the conversation with a model-written summary.
        /// Ctrl+C stops it.
        /// </summary>
        private void Compact()
        {
            Cancel.Begin();
            bool compacted = _client.CompactConversation(_conversation, _settings);
            Cancel.End();

            if (compacted)
                AutoSave(Program.Config.Get(AppSettings.ChatHistoryLimit));
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
                ChatTranscript.Save(arguments.Trim().Trim('"'), _title, _registry.ResolveWorkingDirectory(), _conversation);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error saving chat: " + ex.Message);
            }
            ChatOutput.MarkSeparated();
            return true;
        }

        /// <summary>
        /// Continues a saved chat in the workspace it was made in; later turns save
        /// back to the same file. If that folder no longer exists, the chat is
        /// imported into the current workspace instead.
        /// </summary>
        private bool Load(string arguments)
        {
            if (arguments == null)
                return false;

            string path, title, workspace;
            List<LLMClient.ChatMessage> loaded;
            if (!TryReadChat(arguments, out path, out loaded, out title, out workspace))
                return true;

            _registry.WorkingDirectoryOverride = null;
            if (!string.IsNullOrEmpty(workspace) && Directory.Exists(workspace))
            {
                _registry.WorkingDirectoryOverride = ChatWorkspace.Normalize(workspace);
                Restore(loaded, title, path);
                return true;
            }

            string current = _registry.ResolveWorkingDirectory();
            int historyLimit = Program.Config.Get(AppSettings.ChatHistoryLimit);
            if (historyLimit > 0)
            {
                Console.Error.WriteLine("Chat workspace '" + workspace + "' not found; imported into " + current);
                ImportLoaded(loaded, title, historyLimit);
                return true;
            }

            // No history to import into: continue here.
            Console.Error.WriteLine("Chat workspace '" + workspace + "' not found; using " + current);
            Restore(loaded, title, path);
            return true;
        }

        /// <summary>
        /// Copies a chat into the current workspace's history and continues it there.
        /// The original file is left as it was.
        /// </summary>
        private bool Import(string arguments)
        {
            if (arguments == null)
                return false;

            int historyLimit = Program.Config.Get(AppSettings.ChatHistoryLimit);
            if (historyLimit <= 0)
            {
                Console.Error.WriteLine("Error importing chat: chat history is off (chathistorylimit=0).");
                ChatOutput.MarkSeparated();
                return true;
            }

            string path, title, workspace;
            List<LLMClient.ChatMessage> loaded;
            if (!TryReadChat(arguments, out path, out loaded, out title, out workspace))
                return true;

            ImportLoaded(loaded, title, historyLimit);
            return true;
        }

        // A new file in the current workspace's history, saved right away.
        private void ImportLoaded(List<LLMClient.ChatMessage> loaded, string title, int historyLimit)
        {
            Restore(loaded, title, ChatHistory.NewSessionPath(_registry.ResolveWorkingDirectory()));
            AutoSave(historyLimit);
        }

        private static bool TryReadChat(string arguments, out string path,
            out List<LLMClient.ChatMessage> loaded, out string title, out string workspace)
        {
            path = null;
            loaded = null;
            title = null;
            workspace = null;
            try
            {
                path = Path.GetFullPath(arguments.Trim().Trim('"'));
                loaded = ChatTranscript.Load(path, out title, out workspace);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error loading chat: " + ex.Message);
                ChatOutput.MarkSeparated();
                return false;
            }
        }

        private void Restore(List<LLMClient.ChatMessage> loaded, string title, string sessionPath)
        {
            _conversation.Clear();
            _conversation.AddRange(loaded);
            _title = title;
            _sessionPath = sessionPath;
            _client.PublishStatusTokens(
                LLMClient.GetConversationCharacterCount(_conversation) + _client.GetBaseCharacterOverhead());

            // No blank line before the replay: the GUI would show it at the top of the window.
            if (!_showBanners)
                ChatOutput.MarkSeparated();
            Replay();
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
            ChatOutput.WriteLine("| /compact: Summarize chat to free up context     |");
            ChatOutput.WriteLine("| /exit: Exit the application                     |");
            ChatOutput.WriteLine("| /image \"path\" prompt: Send an image             |");
            ChatOutput.WriteLine("| /import <path>: Copy a chat into this workspace |");
            ChatOutput.WriteLine("| /load <path>: Load a saved chat                 |");
            ChatOutput.WriteLine("| /reasoning [effort]: Set reasoning effort       |");
            ChatOutput.WriteLine("| /reload: Reload configuration                   |");
            ChatOutput.WriteLine("| /save <path>: Save chat as JSON                 |");
            ChatOutput.WriteLine("| Ctrl+C: Stop the response in progress           |");
            ChatOutput.WriteLine("===================================================");
        }
    }
}
