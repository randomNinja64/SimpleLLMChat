<p align="center">
	<img src="SimpleLLMChatGUI/sllmc.ico" alt="SLLMC Icon" width="120" />
</p>
<h3 align="center">SimpleLLMChat</h3>
<p align="center"><em>AI Chatbot/Agent for Windows XP+</em></p>

## About

SimpleLLMChat is a lightweight C# CLI and GUI application that makes LLMs accessible on both legacy and modern Windows systems. The software is designed to work with OpenAI-compatible endpoints and can be extended via a tool system.

## Features
- **Customizable GUI** - The GUI's colors and fonts can be customized as desired.
- **Desktop Assistant Mode** - When enabled, a global Ctrl+Shift+D hotkey can be used to capture a screenshot of the current active window and pass it to the LLM.
- **GUI/CLI Modes** - The software can be used either via its REPL CLI or a GUI wrapper.
- **Image Support** - Images can be passed to supported models via both the CLI and GUI.
- **Markdown Rendering** - The GUI can render Markdown formatting for easier reading.
- **OpenAI-Compatible Endpoint Support** - In addition to OpenAI, any service/server offering the OpenAI v1 chat endpoint can be used, including [llama.cpp](https://github.com/ggerganov/llama.cpp) and [LM Studio](https://lmstudio.ai/).
- **Script-Friendly** - Output without tool calls and reasoning can be piped out of the CLI using the `-o` or `--output-only` flag.
- **Streaming Responses** - Responses are streamed from the LLM server in real-time.
- **Tools** - A modular, extensible tool system is available. For information about the included tools, see [Tools](#tools)

## Requirements

- Access to the OpenAI API or a compatible LLM server
  - **Note**: If your server or endpoint requires modern TLS that the OS does not support, a `curl.exe` and `ca-bundle.crt` can be placed in the same folder as the application executable as a fallback (already included in the XP release build).
- Windows XP (SP3 recommended) or later


## Setup

To use the application, download and extract the appropriate build for your system from the Releases page (builds ending in -XP are recommended for Windows XP systems).

To initialize the configuration file (LLMSettings.ini), open `SimpleLLMChatGUI.exe` and select `Options`. Make sure to set LLM server settings in `System`. Then, set other options as desired.

Once the options have been set, the software can be used via the `SimpleLLMChatCLI.exe` or `SimpleLLMChatGUI.exe` executables.

## Building

The project can be built via the included `build.bat` script (Visual Studio/Visual Studio Build Tools/MSBuild required).

Alternatively, the project can be built using the `SimpleLLMChat.sln` file directly in Visual Studio.

Both methods require the .NET 4.0 Targeting Pack to be installed.

**Note**: Builds generated using these methods include only the core executables. Some optional features (such as the cURL fallback or tool-specific dependencies) require additional executables that are not bundled automatically. Tool-specific dependencies are listed in the [Tools](#tools) section.

## Configuration

SimpleLLMChat can be configured either using the GUI options pages or manually via `LLMSettings.ini`. The options available within the INI file are as follows:

**Appearance**
- `assistantname`: Display name for assistant responses
- `codeblockfontfamily`: Font used for rendering code blocks in the GUI
- `customfontfamily`: Font used for all non-code block text in the GUI
- `fontsize`: Font size for chat and input text
- `markdownparsing`: Enables/disables Markdown rendering in the GUI (`1` or `0`)
- `showreasoningoutput`: Enables/disables reasoning output display (`1` or `0`)
- `showtooloutput`: Enables/disables full tool execution output in chat (`1` or `0`)

**System**
- `apikey`: API key (if required by model provider)
- `llmserver`: Base URL of the OpenAI-compatible endpoint
- `model`: Model name to use for text generation (if supported by the endpoint)
- `sysprompt`: System prompt for the LLM

**Tools**
- `tools`: Comma-separated list of tools the AI is allowed to use
- `toolsrequiringapproval`: Comma-separated list of tools that require manual approval for the AI to use
- `tooltimeout.<toolname>`: Timeout for tool-call (per-tool) in seconds

Tool-specific configuration options for the included tools are available in the [Tools](#tools) section.

## Usage

## Tools

## Troubleshooting

## Credits