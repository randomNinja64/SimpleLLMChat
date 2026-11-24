using System.Collections.Generic;
using System.Windows;

namespace SimpleLLMChatGUI
{
    public partial class Options
    {
        private string _serverUrl;
        private string _apiKey;
        private string _model;
        private string _sysPrompt;
        private int _contextWindowSize;
        private int _chatHistoryLimit;
        private bool _chatTitlesByModel;
        private string _assistantName;
        private string _thinkingDisplayModeText;
        private string _toolCallDisplayModeText;
        private string _toolOutputDisplayModeText;
        private bool _markdownParsing;
        private string _codeFontFamily;
        private string _customFontFamily;
        private int _chatFontSize;

        private bool _ragEnabled;
        private bool _ragChatHistory;
        private string _ragKnowledgePath;
        private int _ragMaxResults;
        private int _indexChunkLines;
        private int _indexChunkOverlap;
        private int _ragMaxSnippetLength;
        private string _ragRetrieveMode;
        private string _ragAllowedExtensions;
        private string _embeddingsEndpoint;
        private string _embeddingsModel;
        private string _embeddingsApiKey;
        private string _jevBaseUrl;
        private string _jevApiKey;
        private string _jevModel;
        private bool _showJevConfidence;
        private int _maxConsecutiveFailedToolCalls;

        private static readonly List<string> DisplayModeOptionList =
            new List<string> { "Shown", "Collapsed", "Hidden" };

        public List<string> SystemFonts { get { return _systemFonts; } }
        public List<string> DisplayModeOptions { get { return DisplayModeOptionList; } }

        public string ServerURL { get => _serverUrl; set => Set(ref _serverUrl, value, nameof(ServerURL)); }
        public string ApiKey { get => _apiKey; set => Set(ref _apiKey, value, nameof(ApiKey)); }
        public string Model { get => _model; set => Set(ref _model, value, nameof(Model)); }
        public string SysPrompt { get => _sysPrompt; set => Set(ref _sysPrompt, value, nameof(SysPrompt)); }
        public int ContextWindowSize { get => _contextWindowSize; set => Set(ref _contextWindowSize, value, nameof(ContextWindowSize)); }
        public int ChatHistoryLimit { get => _chatHistoryLimit; set => Set(ref _chatHistoryLimit, value < 0 ? 0 : value, nameof(ChatHistoryLimit)); }
        public bool ChatTitlesByModel { get => _chatTitlesByModel; set => Set(ref _chatTitlesByModel, value, nameof(ChatTitlesByModel)); }
        public string AssistantName { get => _assistantName; set => Set(ref _assistantName, value, nameof(AssistantName)); }
        public string ThinkingDisplayModeText { get => _thinkingDisplayModeText; set => Set(ref _thinkingDisplayModeText, value, nameof(ThinkingDisplayModeText)); }
        public string ToolCallDisplayModeText { get => _toolCallDisplayModeText; set => Set(ref _toolCallDisplayModeText, value, nameof(ToolCallDisplayModeText)); }
        public string ToolOutputDisplayModeText { get => _toolOutputDisplayModeText; set => Set(ref _toolOutputDisplayModeText, value, nameof(ToolOutputDisplayModeText)); }
        public bool MarkdownParsing { get => _markdownParsing; set => Set(ref _markdownParsing, value, nameof(MarkdownParsing)); }
        public string CodeFontFamily { get => _codeFontFamily; set => Set(ref _codeFontFamily, value, nameof(CodeFontFamily)); }
        public string CustomFontFamily { get => _customFontFamily; set => Set(ref _customFontFamily, value, nameof(CustomFontFamily)); }
        public int ChatFontSize { get => _chatFontSize; set => Set(ref _chatFontSize, FontHandler.ClampFontSize(value), nameof(ChatFontSize)); }

        public bool RagEnabled { get => _ragEnabled; set => Set(ref _ragEnabled, value, nameof(RagEnabled)); }
        public bool RagChatHistory { get => _ragChatHistory; set => Set(ref _ragChatHistory, value, nameof(RagChatHistory)); }
        public string RagKnowledgePath { get => _ragKnowledgePath; set => Set(ref _ragKnowledgePath, value, nameof(RagKnowledgePath)); }
        public int RagMaxResults { get => _ragMaxResults; set => Set(ref _ragMaxResults, value, nameof(RagMaxResults)); }
        public int IndexChunkLines { get => _indexChunkLines; set => Set(ref _indexChunkLines, value, nameof(IndexChunkLines)); }
        public int IndexChunkOverlap { get => _indexChunkOverlap; set => Set(ref _indexChunkOverlap, value, nameof(IndexChunkOverlap)); }
        public int RagMaxSnippetLength { get => _ragMaxSnippetLength; set => Set(ref _ragMaxSnippetLength, value, nameof(RagMaxSnippetLength)); }
        public string RagRetrieveMode { get => _ragRetrieveMode; set => Set(ref _ragRetrieveMode, value, nameof(RagRetrieveMode)); }
        public string RagAllowedExtensions { get => _ragAllowedExtensions; set => Set(ref _ragAllowedExtensions, value, nameof(RagAllowedExtensions)); }
        public string EmbeddingsEndpoint { get => _embeddingsEndpoint; set => Set(ref _embeddingsEndpoint, value, nameof(EmbeddingsEndpoint)); }
        public string EmbeddingsModel { get => _embeddingsModel; set => Set(ref _embeddingsModel, value, nameof(EmbeddingsModel)); }
        public string EmbeddingsApiKey { get => _embeddingsApiKey; set => Set(ref _embeddingsApiKey, value, nameof(EmbeddingsApiKey)); }
        public string JevBaseUrl { get => _jevBaseUrl; set => Set(ref _jevBaseUrl, value, nameof(JevBaseUrl)); }
        public string JevApiKey { get => _jevApiKey; set => Set(ref _jevApiKey, value, nameof(JevApiKey)); }
        public string JevModel { get => _jevModel; set => Set(ref _jevModel, value, nameof(JevModel)); }
        public bool ShowJevConfidence { get => _showJevConfidence; set => Set(ref _showJevConfidence, value, nameof(ShowJevConfidence)); }
        public int MaxConsecutiveFailedToolCalls { get => _maxConsecutiveFailedToolCalls; set => Set(ref _maxConsecutiveFailedToolCalls, value < 0 ? 0 : value, nameof(MaxConsecutiveFailedToolCalls)); }

        private void Set<T>(ref T field, T value, string propertyName)
        {
            field = value;
            OnPropertyChanged(propertyName);
        }

        private void InitializeDefaults()
        {
            ServerURL = AppSettings.LlmServer.Default;
            ApiKey = AppSettings.ApiKey.Default;
            Model = AppSettings.Model.Default;
            SysPrompt = AppSettings.SysPrompt.Default;
            ContextWindowSize = AppSettings.ContextWindowSize.Default;
            ChatHistoryLimit = AppSettings.ChatHistoryLimit.Default;
            ChatTitlesByModel = AppSettings.ChatTitlesByModel.Default;
            AssistantName = AppSettings.AssistantName.Default;
            ThinkingDisplayModeText = AppSettings.ThinkingDisplay.Default.ToString();
            ToolCallDisplayModeText = AppSettings.ToolCallDisplay.Default.ToString();
            ToolOutputDisplayModeText = AppSettings.ToolOutputDisplay.Default.ToString();
            MarkdownParsing = AppSettings.MarkdownParsing.Default;
            CodeFontFamily = AppSettings.CodeBlockFontFamily.Default;
            CustomFontFamily = AppSettings.CustomFontFamily.Default;
            ChatFontSize = AppSettings.FontSize.Default;
            RagEnabled = AppSettings.RagEnabled.Default;
            RagChatHistory = AppSettings.RagChatHistory.Default;
            RagKnowledgePath = AppSettings.RagKnowledgePath.Default;
            RagMaxResults = AppSettings.RagMaxResults.Default;
            IndexChunkLines = AppSettings.IndexChunkLines.Default;
            IndexChunkOverlap = AppSettings.IndexChunkOverlap.Default;
            RagMaxSnippetLength = AppSettings.RagMaxSnippetLength.Default;
            RagRetrieveMode = AppSettings.RagRetrieveMode.Default;
            RagAllowedExtensions = AppSettings.RagAllowedExtensions.Default;
            EmbeddingsEndpoint = AppSettings.EmbeddingsEndpoint.Default;
            EmbeddingsModel = AppSettings.EmbeddingsModel.Default;
            EmbeddingsApiKey = AppSettings.EmbeddingsApiKey.Default;
            JevBaseUrl = AppSettings.JevBaseUrl.Default;
            JevApiKey = AppSettings.JevApiKey.Default;
            JevModel = AppSettings.JevModel.Default;
            ShowJevConfidence = AppSettings.ShowJevConfidence.Default;
            MaxConsecutiveFailedToolCalls = AppSettings.MaxConsecutiveFailedToolCalls.Default;
        }

        private void SaveIni(string path)
        {
            var sections = new List<KeyValuePair<string, List<string>>>
            {
                new KeyValuePair<string, List<string>>(AppSettings.SectionAppearance, GetAppearanceSettings()),
                new KeyValuePair<string, List<string>>(AppSettings.SectionRag, GetRagSettings()),
                new KeyValuePair<string, List<string>>(AppSettings.SectionSystem, GetSystemSettings()),
                new KeyValuePair<string, List<string>>(AppSettings.SectionTools, GetToolSettings()),
            };
            AddDynamicToolSettings(sections);
            SettingsIniWriter.WriteSections(path, sections);
        }

        private List<string> GetAppearanceSettings()
        {
            return new List<string>
            {
                AppSettings.AssistantName.Line(AssistantName),
                AppSettings.CodeBlockFontFamily.Line(CodeFontFamily),
                AppSettings.CustomFontFamily.Line(CustomFontFamily),
                AppSettings.FontSize.Line(ChatFontSize),
                AppSettings.MarkdownParsing.Line(MarkdownParsing ? "1" : "0"),
                AppSettings.ThinkingDisplay.Line((ThinkingDisplayModeText ?? AppSettings.ThinkingDisplay.DefaultText).ToLowerInvariant()),
                AppSettings.ToolCallDisplay.Line((ToolCallDisplayModeText ?? AppSettings.ToolCallDisplay.DefaultText).ToLowerInvariant()),
                AppSettings.ToolOutputDisplay.Line((ToolOutputDisplayModeText ?? AppSettings.ToolOutputDisplay.DefaultText).ToLowerInvariant()),
            };
        }

        private List<string> GetSystemSettings()
        {
            return SettingsIniWriter.GetSystemSettings(ApiKey, ServerURL, Model, SysPrompt, ContextWindowSize, ChatHistoryLimit, ChatTitlesByModel);
        }

        private List<string> GetRagSettings()
        {
            return new List<string>
            {
                AppSettings.RagEnabled.Line(RagEnabled ? "1" : "0"),
                AppSettings.RagChatHistory.Line(RagChatHistory ? "1" : "0"),
                AppSettings.RagAllowedExtensions.Line(RagExtensionList.FormatForStorage(RagAllowedExtensions)),
                AppSettings.IndexChunkOverlap.Line(IndexChunkOverlap),
                AppSettings.IndexChunkLines.Line(IndexChunkLines),
                AppSettings.EmbeddingsApiKey.Line(EmbeddingsApiKey ?? string.Empty),
                AppSettings.EmbeddingsEndpoint.Line(EmbeddingsEndpoint ?? string.Empty),
                AppSettings.EmbeddingsModel.Line(EmbeddingsModel ?? string.Empty),
                AppSettings.RagKnowledgePath.Line(RagKnowledgePath ?? string.Empty),
                AppSettings.RagMaxSnippetLength.Line(RagMaxSnippetLength),
                AppSettings.RagMaxResults.Line(RagMaxResults),
                AppSettings.RagRetrieveMode.Line(RagRetrieveMode ?? AppSettings.RagRetrieveMode.Default),
            };
        }
    }
}
