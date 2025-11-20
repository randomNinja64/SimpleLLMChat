SimpleLLMChat
================

A lightweight C# command-line client for interacting with OpenAI-compatible LLM endpoints. The software can be ran in interactive mode (with contextual awareness), called directly from the CLI, or used via the GUI wrapper.

------------------------------------------------------------
Features
------------------------------------------------------------

- Interactive CLI
- GUI Wrapper Available
- Contextual Awareness
- Supports OAI-compatible endpoints
- Streams assistant responses as they are generated (realtime)
- Tools (See "Tools" section)

------------------------------------------------------------
Requirements
------------------------------------------------------------

- Windows XP or above to run the client
- An OpenAI compatible LLM Server (LM Studio, etc.) (Endpoints requiring TLS 1.2 may not work properly on older systems)
- For the web-based tools, a curl.exe and yt-dlp.exe is needed in the program's folder (Not Included)

------------------------------------------------------------
Configuration
------------------------------------------------------------

LLMSettings.ini is explained below.

apiKey= test (API key if required by your endpoint)
assistantname= LLM (Display name for the assistant in the UI)
llmserver= (OpenAI-compatible endpoint in the format http://ip:port)
maxcontentlength= (number of characters to load from a file/webpage, adjust according to your context window)
model= modelname (model to load (if supported by your endpoint))
searxnginstance= (optional, SearXNG JSON API is supported for web search)
showtooloutput= 0/1 (controls whether or not the full output of tool calls is shown)
sysprompt= "" (custom system prompt)
tools= (a comma separated list of tools the AI is allowed to use)

------------------------------------------------------------
Tools (Available Tools/Requirements)
------------------------------------------------------------

copy_file: Copies a file on the user's PC.
delete_file: Deletes a file from the user's PC.
download_file: Downloads a file from the internet using cURL
(Requires a curl.exe and preferably a ca-bundle.crt in the same folder as the exe)
download_video: Downloads a video from the internet using YT-DLP (Requires a yt-dlp.exe in the same folder as the exe)
extract_file: Extracts an archive using 7-Zip (Requires a 7za.exe in the same folder as the exe)
list_directory: Lists the files and folders in a given path.
move_file: Moves a file on the user's PC.
read_file: Reads a file from the user's PC.
read_website: Retrieves a cleaned up version of a website's HTML (Requires a curl.exe and preferably a ca-bundle.crt in the same folder as the exe)
run_python_script: Creates a python script and runs it. (Requires Python installed to system PATH)
run_shell_command: Runs a shell command (Enable with caution)
run_web_search: Searches the web using SearXNG with DuckDuckGo and Wiby as fallbacks (Requires a curl.exe and preferably a ca-bundle.crt in the same folder as the exe)
write_file: Writes a file to the user's PC.

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
