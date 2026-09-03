![Physalia](Images/physalia.svg)

**Build Grasshopper definitions by talking to an LLM, and customise how the LLM works, not just what it writes.**

[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL--3.0-blue.svg)](LICENSE)
[![Rhino 8](https://img.shields.io/badge/Rhino-8-green.svg)](https://www.rhino3d.com/)

Physalia is a tool for composing agentic loops (and broader orchestrations) where the tool surface is the Rhino/Grasshopper document. In other words, you can customize not only the output of the LLM, but also the process by which the LLM interacts with Grasshopper.

Most AI plugins give you a chat box and a black box behind it. Physalia gives you the loop itself as Grasshopper components, so you can see each step, reorder it, gate it, or swap the model driving it.

<!-- TODO: add a GIF or screenshot here. This is the single highest-value addition to this
     README - most people decide whether to try a Grasshopper plugin from one image. -->

---

## What it does

**Generation**

- Generates Python Script components with correct inputs and outputs from a natural language prompt
- Generates node-based components
- Automatically detects and fixes runtime errors in generated components
- Supports iterative refinement, prompting back and forth to tweak generated components
- Stages larger builds incrementally rather than emitting everything at once

**Context and tools**

- Connects to MCP servers, so the model can reach tools beyond the Grasshopper document
- Reads PDFs as context, useful for working from a spec, a standard, or a paper
- Reusable system prompts, presets, and memories

**Models**

- OpenAI
- Anthropic
- Google Gemini (generous free tier)
- Any OpenAI-protocol endpoint (OpenRouter, DeepSeek, Groq, and many more)
- Local models via Ollama, with no API key or internet connection required
- The Claude Code SDK. If Claude Code is installed, no API key is needed

---

## Requirements

- Rhino 8
- Windows
- An API key for at least one provider, **or** Ollama installed locally, **or** Claude Code installed

---

## Installation

<!-- TODO: fill this in before release. Whichever route you take, people need copy-pasteable
     steps. If you package for Yak this becomes a one-liner; if it is a manual .gha drop,
     spell out the unblock step, which is where most Windows users get stuck. -->

**TODO**: installation instructions.

---

## Configuration

API keys live in `Files/API_KEY_CONFIG.YAML`. Copy the example and fill in the providers you intend to use:

```
cp Files/API_KEY_CONFIG.YAML.example Files/API_KEY_CONFIG.YAML
```

MCP servers are configured the same way, via `Files/MCP_SERVERS.YAML`:

```
cp Files/MCP_SERVERS.YAML.example Files/MCP_SERVERS.YAML
```

Neither file is tracked by git. Do not commit your keys.

---

## Quick start

<!-- TODO: three or four steps from "installed" to "first generated component", ideally with
     the prompt text you would actually type. -->

**TODO**: quick start walkthrough.

---

## License

Physalia is designed as a free, auditable alternative to paid LLM plugins for Grasshopper. The AGPL-3.0 license means it stays open. Any fork that ships must also stay open.

[AGPL-3.0](LICENSE)

---

## Issues and contributions

Bug reports and feature requests are welcome via [GitHub Issues](https://github.com/aarcThom/Physalia/issues).
