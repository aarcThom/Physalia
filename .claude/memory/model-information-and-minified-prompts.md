---
name: model-information-and-minified-prompts
description: Model Information now merges OpenRouter+LiteLLM with id normalization; minified preambles/schemas added for small models
metadata: 
  node_type: memory
  type: project
  originSessionId: c4ef98d3-9f0b-4831-a5c4-409d6c488096
---

Two fixes landed 2026-06-27 (build clean, live test pending). Full notes: `planning/model-information.md`.

**Model Information fix:** was LiteLLM-only with thin id matching → "not found" for most models. Rewrote `Physalia.Core/Models/ModelList.cs` to a merged **OpenRouter (`/api/v1/models`, no-auth, ~400+ models, primary) + LiteLLM (gap-filler)** resolver. `FetchAsync` fetches both, resilient (one source failing still works; total failure errors). Each entry indexed under all normalized lookup-key variants (`LookupKeys`/`Canonicalize`: lowercase, strip provider prefix, last path segment, strip `:free`/date suffix, `-N-M`→`N.M`), so Anthropic dated `claude-sonnet-4-5-20250929` resolves to OR `anthropic/claude-sonnet-4.5`. Component outputs unchanged (Max Input/Output, Image/Tool capable). Future: local providers (Ollama `/api/show`, llama.cpp `/props`) for local GGUF models; native Anthropic/Gemini endpoints; bundled models.dev static fallback; surface pricing + reasoning flag (ModelEntry already has SupportsReasoning).

**Minified prompts (small/local/free models):** added to `Files/SYSTEM_PROMPTS/` next to originals (Composer's picker lists the folder, so they appear automatically; build pipeline copies Files→bin). New: `PREAMBLE/Node Graph (Minified).txt`, `PREAMBLE/Python3 Script (Minified).txt`, `SCHEMA/PhySchema (Minified).json`, `SCHEMA/Python3Schema (Minified).json`. Schemas stay **valid JSON Schema** (the Composer's Schema output feeds the Auditor's SchemaValidator) — minification = drop verbose `description`s, keep structure + a trimmed `componentCatalog`/`rules` + ONE compact example (small models follow examples best). The "node schema" = PhySchema.json (the node-graph authoring schema), NOT GhJSONSchema.json (the library format).

**Research deliverables (not implemented):** `planning/deterministic-gates.md` (gate catalog; first 3 = Retry Limiter, JSON Well-Formedness, Python Syntax) and `planning/tool-components.md` (tool catalog; first 5 = get_document_summary, inspect_component, query_geometry, place_graph, run_python). Both ground heavily on existing bases (RoutingComponentBase/SignalLimiter; ToolComponentBase/GhJsonBridge).
