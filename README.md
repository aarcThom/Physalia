![Physalia](Images/physalia.svg)

Physalia is a tool for composing agentic loops (and broader orchestrations) where the tool surface is the Rhino/Grasshopper document. In other words, you can customize not only the output of the LLM, but also the process by which the LLM interacts with Grasshopper.

---

## What it does

- Generates Python Script components with correct inputs and outputs from a natural language prompt
- Generates node-based components
- Automatically detects and fixes runtime errors in generated components
- Supports iterative refinement — prompt back and forth to tweak generate components
- Supports the following APIs:
   - OpenAI
   - Anthropic
   - Google Gemini (generous free tier)
   - OpenAI API protocol (OpenRouter, Deepseek, and many more.)
- Runs local models via Ollama — no API key or internet connection required
- Runs via the Claude Code SDK — if you have Claude Code installed, no API key is needed.

---

## License

Physalia is designed as a free, auditable alternative to paid LLM plugins for Grasshopper. The AGPL-3.0 license means it stays open — any fork that ships must also stay open.

[AGPL-3.0](LICENSE)
