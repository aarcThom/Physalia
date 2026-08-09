# Physalia memories

This folder backs the model-invoked **Memory** tool. Files here are the model's persistent memory —
they survive across conversations.

Layout:

- `GLOBAL/` — memories shared across **every** Grasshopper document.
- `LOCAL/<document-key>/` — memories specific to a single Grasshopper document. The `<document-key>`
  is derived from the document's file (name + a short hash of its path), so each `.gh` file gets its
  own memory folder. Unsaved documents use a shared `untitled` folder for the session.

The model addresses these through a virtual `/memories` path: `/memories/global/...` and
`/memories/local/...`. That virtual scheme is the model-facing API and stays lower-case — it is
matched case-insensitively, so it is unaffected by what these folders are named on disk. Memories are
plain Markdown (`.md`) files.

The Memory tool only informs the model that this memory exists when a **Memory Grounding** component is
wired into the Conversation Log. Without that grounding, the model is told nothing about memory.
