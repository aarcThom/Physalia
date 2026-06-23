---
name: physalia-repo-gotchas
description: "Stale paths in CLAUDE.md (slnx lives in src/, primitives doc in planning/) and build-induced EOL noise in git status"
metadata: 
  node_type: memory
  type: project
  originSessionId: bfb2ff92-c812-4e27-89de-9675e357c091
---

CLAUDE.md has stale paths:
- The solution file is `src/Physalia.slnx`, not repo-root `Physalia.slnx` — build with
  `dotnet build src/Physalia.slnx`.
- `docs/physalia-primitives.md` and `docs/ghjson-implementation.md` actually live in
  `planning/` (`planning/physalia-primitives.md`).

**Why:** following CLAUDE.md verbatim fails (MSB1009) or misses the doc.
**How to apply:** use the `src/` and `planning/` paths; consider fixing CLAUDE.md when touching it.

Also: running a build leaves `src/Physalia.GH/Files/PhySchema.json` and
`agent_guides/physchema_requirements.md` showing as modified in `git status` with an
**empty diff** — it's CRLF/LF normalization noise, not a real change. Don't include them
in commits or describe them as changes. Related: [[trigger-state-machine-status]].

Svelte chat UI build (`src/Physalia.UI`, a no-assembly MSBuild wrapper around Vite that
emits `Files/UI/chat.html`, gitignored): Visual Studio's fast up-to-date check skips
projects with no managed output, so in VS the `BuildPhysaliaUI` target never ran and
chat.html was never generated/copied to the GH debug folder — while `dotnet build` (no
FUTDC) worked fine. Fixed 2026-06-22 by adding `<DisableFastUpToDateCheck>true</…>` to
`Physalia.UI.csproj`, and splitting npm restore into a lockfile-incremental
`RestorePhysaliaUI` target using `npm ci` (the old `!Exists(node_modules)` guard never
reinstalled after a dependency bump).
**How to apply:** if the chat window shows "UI not found", confirm chat.html exists in
`Files/UI/` and in `bin/<Config>/Files/UI/`; a no-output wrapper project skipped by VS is
the usual cause.
