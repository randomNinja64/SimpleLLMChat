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
        private string _assistantName;
        private string _thinkingDisplayModeText = "Collapsed";
        private string _toolCallDisplayModeText = "Collapsed";
        private string _toolOutputDisplayModeText = "Shown";
        private bool _markdownParsing;
        private string _codeFontFamily;
        private string _customFontFamily;
        private int _chatFontSize;

        private bool _ragEnabled;
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

        private static readonly List<string> DisplayModeOptionList =
            new List<string> { "Shown", "Collapsed", "Hidden" };

        public List<string> AvailableTools
        {
            get { return _availableTools; }
        }

        public List<string> SystemFonts
        {
            get { return _systemFonts; }
        }

        public string ServerURL
        {
            get { return _serverUrl; }
            set { _serverUrl = value; OnPropertyChanged(nameof(ServerURL)); }
        }

        public string ApiKey
        {
            get { return _apiKey; }
            set { _apiKey = value; OnPropertyChanged(nameof(ApiKey)); }
        }

        public string Model
        {
            get { return _model; }
            set { _model = value; OnPropertyChanged(nameof(Model)); }
        }

        public string SysPrompt
        {
            get { return _sysPrompt; }
            set { _sysPrompt = value; OnPropertyChanged(nameof(SysPrompt)); }
        }

        public int ContextWindowSize
        {
            get { return _contextWindowSize; }
            set { _contextWindowSize = value; OnPropertyChanged(nameof(ContextWindowSize)); }
        }

        public string AssistantName
        {
            get { return _assistantName; }
            set { _assistantName = value; OnPropertyChanged(nameof(AssistantName)); }
        }

        public List<string> DisplayModeOptions
        {
            get { return DisplayModeOptionList; }
        }

        public string ThinkingDisplayModeText
        {
            get { return _thinkingDisplayModeText; }
            set { _thinkingDisplayModeText = value; OnPropertyChanged(nameof(ThinkingDisplayModeText)); }
        }

        public string ToolCallDisplayModeText
        {
            get { return _toolCallDisplayModeText; }
            set { _toolCallDisplayModeText = value; OnPropertyChanged(nameof(ToolCallDisplayModeText)); }
        }

        public string ToolOutputDisplayModeText
        {
            get { return _toolOutputDisplayModeText; }
            set { _toolOutputDisplayModeText = value; OnPropertyChanged(nameof(ToolOutputDisplayModeText)); }
        }

        public bool MarkdownParsing
        {
            get { return _markdownParsing; }
            set { _markdownParsing = value; OnPropertyChanged(nameof(MarkdownParsing)); }
        }

        public string CodeFontFamily
        {
            get { return _codeFontFamily; }
            set { _codeFontFamily = value; OnPropertyChanged(nameof(CodeFontFamily)); }
        }

        public string CustomFontFamily
        {
            get { return _customFontFamily; }
            set { _customFontFamily = value; OnPropertyChanged(nameof(CustomFontFamily)); }
        }

        public int ChatFontSize
        {
            get { return _chatFontSize; }
            set { _chatFontSize = value; OnPropertyChanged(nameof(ChatFontSize)); }
        }

        public bool RagEnabled
        {
            get { return _ragEnabled; }
            set { _ragEnabled = value; OnPropertyChanged(nameof(RagEnabled)); }
        }

        public string RagKnowledgePath
        {
            get { return _ragKnowledgePath; }
            set { _ragKnowledgePath = value; OnPropertyChanged(nameof(RagKnowledgePath)); }
        }

        public int RagMaxResults
        {
            get { return _ragMaxResults; }
            set { _ragMaxResults = value; OnPropertyChanged(nameof(RagMaxResults)); }
        }

        public int IndexChunkLines
        {
            get { return _indexChunkLines; }
            set { _indexChunkLines = value; OnPropertyChanged(nameof(IndexChunkLines)); }
        }

        public int IndexChunkOverlap
        {
            get { return _indexChunkOverlap; }
            set { _indexChunkOverlap = value; OnPropertyChanged(nameof(IndexChunkOverlap)); }
        }

        public int RagMaxSnippetLength
        {
            get { return _ragMaxSnippetLength; }
            set { _ragMaxSnippetLength = value; OnPropertyChanged(nameof(RagMaxSnippetLength)); }
        }

        public string RagRetrieveMode
        {
            get { return _ragRetrieveMode; }
            set { _ragRetrieveMode = value; OnPropertyChanged(nameof(RagRetrieveMode)); }
        }

        public string RagAllowedExtensions
        {
            get { return _ragAllowedExtensions; }
            set { _ragAllowedExtensions = value; OnPropertyChanged(nameof(RagAllowedExtensions)); }
        }

        public string EmbeddingsEndpoint
        {
            get { return _embeddingsEndpoint; }
            set { _embeddingsEndpoint = value; OnPropertyChanged(nameof(EmbeddingsEndpoint)); }
        }

        public string EmbeddingsModel
        {
            get { return _embeddingsModel; }
            set { _embeddingsModel = value; OnPropertyChanged(nameof(EmbeddingsModel)); }
        }

        public string EmbeddingsApiKey
        {
            get { return _embeddingsApiKey; }
            set { _embeddingsApiKey = value; OnPropertyChanged(nameof(EmbeddingsApiKey)); }
        }

        private void InitializeDefaults()
        {
            ServerURL = "";
            ApiKey = "";
            Model = "";
            SysPrompt = "";
            ContextWindowSize = 0;
            AssistantName = AppConstants.DefaultAssistantName;
            ThinkingDisplayModeText = "Collapsed";
            ToolCallDisplayModeText = "Collapsed";
            ToolOutputDisplayModeText = "Shown";
            MarkdownParsing = true;
            CodeFontFamily = "";
            CustomFontFamily = "";
            ChatFontSize = AppConstants.DefaultChatFontSize;
            RagEnabled = false;
            RagKnowledgePath = "";
            RagMaxResults = 5;
            IndexChunkLines = 60;
            IndexChunkOverlap = 10;
            RagMaxSnippetLength = 2000;
            RagRetrieveMode = "newchat";
            RagAllowedExtensions = AppConstants.DefaultRagAllowedExtensions;
            EmbeddingsEndpoint = "";
            EmbeddingsModel = "";
            EmbeddingsApiKey = "";
        }

        private void SaveIni(string path)
        {
            var sections = new List<KeyValuePair<string, List<string>>>
            {
                new KeyValuePair<string, List<string>>("Appearance", GetAppearanceSettings()),
                new KeyValuePair<string, List<string>>("RAG", GetRagSettings()),
                new KeyValuePair<string, List<string>>("System", GetSystemSettings()),
                new KeyValuePair<string, List<string>>("Tools", GetToolSettings()),
            };
            AddDynamicToolSettings(sections);
            SettingsIniWriter.WriteSections(path, sections);
        }

        private List<string> GetAppearanceSettings()
        {
            return new List<string>
            {
                "assistantname=" + AssistantName,
                "codeblockfontfamily=" + CodeFontFamily,
                "customfontfamily=" + CustomFontFamily,
                "fontsize=" + ChatFontSize,
                "markdownparsing=" + (MarkdownParsing ? "1" : "0"),
                "thinkingdisplay=" + (ThinkingDisplayModeText ?? "Collapsed").ToLowerInvariant(),
                "toolcalldisplay=" + (ToolCallDisplayModeText ?? "Collapsed").ToLowerInvariant(),
                "tooloutputdisplay=" + (ToolOutputDisplayModeText ?? "Shown").ToLowerInvariant(),
            };
        }

        private List<string> GetSystemSettings()
        {
            return SettingsIniWriter.GetSystemSettings(ApiKey, ServerURL, Model, SysPrompt, ContextWindowSize);
        }

        private List<string> GetRagSettings()
        {
            string allowedExt = RagExtensionList.FormatForStorage(RagAllowedExtensions);
            return new List<string>
            {
                "ragenabled=" + (RagEnabled ? "1" : "0"),
                "ragallowedextensions=" + allowedExt,
                "indexchunkoverlap=" + IndexChunkOverlap,
                "indexchunklines=" + IndexChunkLines,
                "embeddingsapikey=" + (EmbeddingsApiKey ?? string.Empty),
                "embeddingsendpoint=" + (EmbeddingsEndpoint ?? string.Empty),
                "embeddingsmodel=" + (EmbeddingsModel ?? string.Empty),
                "ragknowledgepath=" + (RagKnowledgePath ?? string.Empty),
                "ragmaxsnippetlength=" + RagMaxSnippetLength,
                "ragmaxresults=" + RagMaxResults,
                "ragretrievemode=" + (RagRetrieveMode ?? "newchat"),
            };
        }
    }
}
