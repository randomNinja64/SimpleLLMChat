SimpleLLMChat
================

A lightweight C++ command-line client for interacting with OpenAI-compatible LLM endpoints. The software can be ran in interactive mode (with contextual awareness) or be called directly from the CLI (GUI coming in a future release).

------------------------------------------------------------
Features
------------------------------------------------------------

- Interactive CLI
- GUI Wrapper Available
- Contextual Awareness
- Supports OAI-compatible endpoints (HTTP only for now)
- Streams assistant responses as they are generated (realtime)

------------------------------------------------------------
Requirements
------------------------------------------------------------

- Windows XP or above to run the client
- An OpenAI compatible LLM Server that accepts HTTP(non-S) requests (LM Studio, etc.)

------------------------------------------------------------
Configuration
------------------------------------------------------------

config.ini is explained below.

host= x.x.x.x (IP address or domain)
port= 1234 (port)
apiKey= test (API key if required by your endpoint)
model= modelname (model to load (if supported by your endpoint))
sysprompt = "" (custom system prompt)
assistantname = LLM (Display name for the assistant in the UI)

------------------------------------------------------------
Usage
------------------------------------------------------------

To run in interactive mode, simply open the executable (CLI or GUI).

To call it from a command line, run SimpleLLMChatCLI.exe <prompt>.

For passing images into the CLI version (provided the model supports it), use:
SimpleLLMChatCLI.exe --image <path> <prompt>

------------------------------------------------------------
License
------------------------------------------------------------

SimpleLLMChat is licensed under the MIT License.