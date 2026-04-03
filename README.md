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
- **Tools** - A modular, extensible tool system is available. For information about the included tools, see [Tools](#tools).

## Requirements

- Access to the OpenAI API or a compatible LLM server
  - **Note**: If your server or endpoint requires modern TLS that the OS does not support, a `curl.exe` and `curl-ca-bundle.crt` can be placed in the same folder as the application executable as a fallback (already included in the XP release build).
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
- `tooltimeout.<toolname>`: Timeout for tool-call in seconds

Tool-specific configuration options for the included tools are available in the [Tools](#tools) section.

## Usage

**GUI**: To launch the GUI interface, open `SimpleLLMChatGUI.exe`.

- To adjust application settings, select `Options`.
- To attach an image, press the image attachment button (camera icon) and choose a file using the file picker.
- To clear the chat, press `Clear Chat`.
- To enable/disable desktop assistant mode, use the `Desktop Assistant` toggle button.
  - To use desktop assistant mode, press `Ctrl+Shift+D`. This will focus the application with a screenshot of your last active window attached, allowing you to ask the LLM about it.
- To send a message, type it into the input box and press `Enter` or `Send`. To create multi-line messages, press `Shift+Enter`.

**CLI**: To run the CLI in interactive/REPL mode, open `SimpleLLMChatCLI.exe` directly or from a terminal.

Interactive mode provides the following commands:
- `clear`: Clears chat context
- `exit`: Exits the application
- `image`: Sends an image to the model alongside the next message, used as `image <path> <prompt>`

The application can also be used in a non-interactive mode.

For a single prompt, use `SimpleLLMChatCLI.exe <prompt>`.

To pass in an image alongside a prompt, use `SimpleLLMChatCLI.exe --image <path> <prompt>`.

CLI mode provides the following additional flags:
- `--no-banners`: Suppresses instructional prompts
- `-o`, `--output-only`: Outputs only the model's response (useful for scripting)

## Tools

SimpleLLMChat includes the following 5 tool packages:

- File Tools
  - Tools
    - `copy_file`: Copies a file from one location to another
    - `delete_file`: Deletes a file from the file system
    - `extract_file`: Extracts an archive to a destination directory
    - `list_directory`: Lists all files and subdirectories in a given directory
    - `move_file`: Moves a file from one location to another
    - `read_file`: Reads the contents of a local file
    - `write_file`: Writes contents to a local file
  - Configuration Options
    - `maxFileContentLength`: Maximum number of characters to read from a file (default: `8000`)
  - Dependencies
    - `7za.exe`: used to extract archives
- Memory Tools
  - Tools
    - `delete_memory`: Deletes a saved memory entry
    - `list_memories`: Lists the names of all saved memory entries
    - `recall_memory`: Reads a saved memory entry
    - `save_memory`: Saves or updates a memory entry
    - `search_memories`: Searches all memory entries by keyword
  - Configuration Options
    - `maxContentLength`: Maximum length of a memory entry (in characters) (default: `2000`)
    - `maxMemories`: Maximum number of memories to store (default: `50`)
- Python Tools
  - Tools
    - `run_python_script`: Creates and executes a Python script
  - Dependencies
    - Python must be installed and available on PATH.
- Shell Tools
  - Tools
    - `run_shell_command`: Executes a shell command
- Web Tools
  - Tools
    - `download_file`: Downloads a file using cURL
    - `download_video`: Downloads an online video using YT-DLP and saves it to the user's desktop
    - `read_website`: Reads the HTML content of a web page
    - `run_web_search`: Searches the web using SearXNG, with DDG and Wiby as fallbacks
  - Configuration Options
    - `maxSearchResults`: Maximum number of search results to retrieve (default: `20`)
    - `maxWebContentLength`: Maximum number of characters to return when reading a webpage (default: `8000`)
    - `SearXNGInstance`: SearXNG instance to use for running web searches (must support JSON API) (default: none)
    - `userAgent`: User agent to use when making web requests (default: `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.5993.90 Safari/537.36`)
  - Dependencies
    - `curl-ca-bundle.crt`: used for cURL
    - `curl.exe`:  used to download web content
    - `yt-dlp.exe`: used to download online videos

**Note**: Dependencies should be placed alongside tool executables. The release builds already include these dependencies, with the exclusion of Python.

Third party tools can be installed by extracting them into the `tools` folder. For more information on developing/distributing custom tools, see https://github.com/randomNinja64/SimpleLLMChat-Tool-SDK

## Troubleshooting

## Credits