---
name: memory-tool
description: The provider-agnostic memory tool + memory grounding (persistent LLM memory) added 2026-07-02
metadata: 
  node_type: memory
  type: project
  originSessionId: 0eca70cc-9bb8-4158-ad22-447c419bac1b
---

Persistent LLM memory feature (built 2026-07-02 on branch `feat/memory-tool`, autonomous session; builds clean, 178 Core tests green, svelte-check clean; **live Rhino test pending**).

**Shape:** an Anthropic-style memory tool (single `memory` function, `command` enum `view/create/str_replace/insert/delete/rename`) reused across all providers — schema is provider-agnostic, backend is one execution layer separate from the schema.

**Files added:**
- `Physalia.Core/Memory/MemoryStore.cs` — the execution backend (`Execute(inputJson, MemoryRoots) → MemoryOutcome`). Virtual `/memories` FS: `/memories/global/...` and `/memories/local/...` map to two physical roots; `..`-escape + out-of-root rejected. Side-effecting file I/O in Core, following the WebTools precedent (Core isn't strictly pure for tool backends).
- `Physalia.Core/ConvoInstruct/PromptMemoryResolver.cs` — normalizes `/m/global` and `/m/local` chat tokens to directive phrases (mirrors [[rhino-geometry-tool-and-slash-t]] `/t/` resolver).
- `Physalia.Core/Grounding/Grounding.cs` — new `MemoryGrounding` record (the system-prompt nudge; parameterless; hits Recorder's `default:` passthrough case).
- `Physalia.GH/Components/Tools/MemoryTool.cs` (`MemoryTool : ToolComponentBase`, sync) + `MemoryLocations.cs` (resolves roots; local dir keyed per-`.gh`-file = sanitized name + SHA256[:8] of path; unsaved → `untitled`).
- `Physalia.GH/Components/Grounding/MemoryGrounder.cs` (`Memory Grounding`, emits `MemoryGrounding`).
- `Files/memories/{global,local}/` (+ README). Runtime path = `assemblyDir/Files/memories` (dev rebuilds overwrite bin copy — same caveat as all Files content).
- Tests: `MemoryStoreTests.cs`, `PromptMemoryResolverTests.cs`.

**Wiring model (matches existing tool + grounding pattern):** Memory tool node → Router loop (callable, advertised via Tools Present/`ToolsInUse`); **Memory Grounding** → Recorder's Grounding input = the switch. Grounding wired ⇒ model told about memory + `/m/global`//`/m/local` enabled in chat; not wired ⇒ model told nothing (fully opt-in — that's why the nudge lives in the grounding, NOT the static preambles).

**Touched:** Recorder (`_liveMemoryGrounding` + `HasMemoryGrounding`), ChatWindow (`NormalizeRefs` adds `PromptMemoryResolver`; Tick state gets `memoryWired`+`memoryScopes`), bridge.ts/App.svelte/Composer.svelte (`/m/` kind, BrainIcon)/Grounding.svelte (read-only Memory page). See [[signal-carrier-discipline]] — memory rides no signal field; it's a tool + grounding.
