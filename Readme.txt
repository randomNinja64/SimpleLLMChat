SimpleLLMChat
================

A lightweight C# command-line client for interacting with OpenAI-compatible LLM endpoints. The software can be ran in interactive mode (with contextual awareness), called directly from the CLI, or used via the GUI wrapper.

------------------------------------------------------------
Features
------------------------------------------------------------

- Interactive CLI
- GUI Wrapper Available
- Contextual Awareness
- Supports OAI-compatible endpoints (HTTP only for now)
- Streams assistant responses as they are generated (realtime)
- Tools (See "Tools" section)

------------------------------------------------------------
Requirements
------------------------------------------------------------

- Windows XP or above to run the client
- An OpenAI compatible LLM Server that accepts HTTP(non-S) requests (LM Studio, etc.)
- For the web-based tools, a curl.exe and yt-dlp.exe is needed in the program's folder (Not Included)

------------------------------------------------------------
Configuration
------------------------------------------------------------

LLMSettings.ini is explained below.

host= x.x.x.x (IP address or domain)
port= 1234 (port)
apiKey= test (API key if required by your endpoint)
model= modelname (model to load (if supported by your endpoint))
sysprompt = "" (custom system prompt)
assistantname = LLM (Display name for the assistant in the UI)
tools=run_shell_command,run_web_search (list of tools the AI is allowed to use)
showtooloutput=0/1 (controls whether or not the full output of tool calls is shown)

------------------------------------------------------------
Tools (Available Tools/Requirements)
------------------------------------------------------------
download_video: Downloads a video from the internet using YT-DLP (Requires a yt-dlp.exe in the same folder as the exe)
read_website: Retrieves a cleaned up version of a website's HTML (Requires a curl.exe and prefereably a ca-bundle.crt in the same folder as the exe)
run_shell_command: Runs a shell command (Enable with caution)
run_web_search: Searches the web using DuckDuckGo (Requires a curl.exe and prefereably a ca-bundle.crt in the same folder as the exe)

------------------------------------------------------------
Usage
------------------------------------------------------------

To run in interactive mode, simply open the executable (CLI or GUI).

To call it from a command line, run SimpleLLMChatCLI.exe <prompt>.

For passing images into the CLI version (provided the model supports it), use:
SimpleLLMChatCLI.exe --image <path> <prompt>

If you'd like to run it without the instruction prompts, run the program with --no-banners

If you'd like the program to output only the LLMs output to your prompt (useful for scripts), call it with --output-only or -o (Ex: SimpleLLMChatCLI.exe -o <prompt>)

Once Desktop Assistant Mode is enabled, it can be triggered using Ctrl+Shift+D.

------------------------------------------------------------
License
------------------------------------------------------------

SimpleLLMChat is licensed under the MIT License.
