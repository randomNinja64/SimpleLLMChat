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
- **OpenAI-Compatible Endpoint Support** - In addition to OpenAI, any service/server offering the OpenAI v1 chat endpoint can be used, including Llama.cpp and LM Studio.
- **Script-Friendly** - Output without tool calls and reasoning can be piped out of the CLI using the `-o` or `--output-only` flag.
- **Streaming Responses** - Responses are streamed from the LLM server in real-time.
- **Tools** - A modular, extensible tool system is available. For information about the included tools, see [Tools](#tools)

## Requirements

## Setup

## Building

## Configuration

## Usage

## Tools

## Troubleshooting

## Credits