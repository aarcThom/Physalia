---
name: memory-tool
description: "The provider-agnostic memory tool (persistent LLM memory) — a plain tool, no grounding"
metadata: 
  node_type: memory
  type: project
  originSessionId: 0eca70cc-9bb8-4158-ad22-447c419bac1b
---

Persistent LLM memory feature (built 2026-07-02 on branch `feat/memory-tool`, autonomous session; builds clean, 175 Core tests green, svelte-check clean; **live Rhino test pending**).

**Shape:** an Anthropic-style memory tool — single `memory` function, `command` enum `view/create/str_replace/insert/delete/rename` — reused across all providers (schema is provider-agnostic; backend is one execution layer separate from the schema).

**It is a PLAIN tool, treated exactly like the other tools** (Thomas's call, 2026-07-02): wire the Memory tool node into a Router; it's advertised by Tools Present / `ToolsInUse` and appears in the Tools grounding list like `web_search` etc. There is **NO memory grounding** — an earlier version had a `MemoryGrounding` + `/m/global`//`/m/local` refs; those were removed as inconsistent with the other tools. The self-describing `ToolDefinition` description carries the "view /memories at the start" nudge + the global/local scope explanation.

**Files (current):**
- `Physalia.Core/Memory/MemoryStore.cs` — execution backend (`Execute(inputJson, MemoryRoots) → MemoryOutcome`). Virtual `/memories` FS: `/memories/global/...` + `/memories/local/...` → two physical roots; `..`-escape + out-of-root rejected. Side-effecting file I/O in Core, per WebTools precedent.
- `Physalia.GH/Components/Tools/MemoryTool.cs` (`MemoryTool : ToolComponentBase`, sync) + `MemoryLocations.cs` (roots; local dir keyed per-`.gh`-file = sanitized name + SHA256[:8] of path; unsaved → `untitled`).
- `Files/memories/{global,local}/` (+ README). Runtime path = `assemblyDir/Files/memories` (dev rebuilds overwrite bin copy — same caveat as all Files content).
- Tests: `MemoryStoreTests.cs`.

**Slash refs:** memory is reached via the existing `/t/` tool reference, extended with a scope for the memory tool ONLY: `/t/memory/global` and `/t/memory/local` (parallels `/c/<tab>/<component>`). Implemented in `PromptToolResolver` (Core — `MatchScope`/`MemoryScopePhrase`, special-cases the name "memory") and in `Composer.svelte` (a `memory-scope` RefStage; accepting "memory" in the tool menu inserts `memory/` and reopens the scope menu; highlight + autocomplete gated on `hasMemoryTool()`). No `/m/` command, no `PromptMemoryResolver`, no memory page/pill in Grounding.svelte, no `memoryWired`/`memoryScopes` in bridge/App/ChatWindow. See [[rhino-geometry-tool-and-slash-t]].
