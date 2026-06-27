---
name: web-search-tools
description: web_search (Tavily) + read_url (Jina Reader) LLM tool components and their API-key wiring
metadata: 
  node_type: memory
  type: project
  originSessionId: c4ef98d3-9f0b-4831-a5c4-409d6c488096
---

Two LLM tool components added 2026-06-27 (builds clean, live-Rhino test pending). Research + provider comparison: `planning/web-tools.md`.

- **`web_search`** (`WebSearch.cs`, Tools tab, GUID 02315974-8633-4BCF-B4B3-9C33DC193778) — searches the internet via **Tavily** (chosen: free 1k/mo, no card, agent-shaped, one key also powers fetch). Input `{query, count}`.
- **`read_url`** (`ReadUrl.cs`, GUID 7F8E6EB2-B012-4068-A90B-D9EF87229B7F) — fetches a URL via **Jina Reader** `r.jina.ai/<url>` → clean markdown, **keyless** (optional jina key raises limits). Input `{url, max_chars}`.
- **Core HTTP** in `Physalia.Core/Web/WebTools.cs` (`SearchTavilyAsync`, `FetchUrlAsync`; pure, `ConfigureAwait(false)`, Result<string,LlmError>, reuses `HttpErrorMapper`). Bing API is dead (retired 2025-08-11); Google CSE closing — both excluded.
- **Keys** resolve via `WebToolKeys.Resolve(provider)` → `Api.GetKeys` over `Files/API_KEY_CONFIG.YAML`, new **`web_search`** section (leaf names `tavily`/`jina` become provider ids; env `TAVILY_API_KEY`/`JINA_API_KEY`). Added to `.example` (committed) and the real local YAML (gitignored, not committed — has live keys). Never serialized.

**Key constraint:** `ToolComponentBase.ExecuteCall` is **synchronous** (called in SolveInstance), so the tools **block** on the async HTTP via `.GetAwaiter().GetResult()` with a CTS timeout. Works (Core uses ConfigureAwait(false), no deadlock) but freezes the GH solve thread for the call duration. Future enhancement: async tool support in ToolComponentBase so web/IO tools don't block. See [[tools-in-use-component]], `planning/tool-components.md`.
