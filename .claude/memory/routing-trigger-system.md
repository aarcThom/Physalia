---
name: routing-trigger-system
description: Signal-based component lifecycle (supersedes the 2026-06 bool-trigger/pulse system) — where the authoritative doc lives and the few facts not in the repo
metadata: 
  node_type: memory
  type: project
  originSessionId: 5c4f2248-d742-4cd1-84cc-2a122f89c65e
---

# Physalia signal lifecycle (reworked 2026-06-10/11, commits 91c83c5 → d6a086c)

The bool-trigger / momentary-pulse / SHA-256-signature system this file used to describe is **gone**. Events now travel as latched, sequence-numbered, consume-once `PhySignal`s whose payload is the only data carrier between pipeline components (one wire per hop; Success Signal(0) / Fail Signal(1); nothing in the lifecycle serializes — components reopen Empty).

**Authoritative reference: `planning/data-marshalling.md` in the repo** (signal semantics, the StatefulComponentBase → RoutingComponentBase two-layer architecture, solve rhythm, Conversation Log identity-based turns, wiring diagram, rules for new components). CLAUDE.md carries a condensed version (updated 2026-06-11). Don't re-derive from this memory — read the repo doc.

Non-obvious facts NOT in the repo docs:

- **PyTransmitter deliberately does not clear its linked target** on Clear Outputs / unlink — the pushed Python code stays in the target component. This is a requirement (vs Schema Validator/SchemaTranslator which only own their latches), not an omission.
- **`PythonComponent.json` lives in repo-root `Files/`** (canonical — `CopyLibraryFiles` globs `..\..\Files\**\*`); a stale duplicate tree exists at `src/Physalia.GH/Files/`, flagged but never reconciled.
- Old serialization keys from the trigger era (`State`, `DataOut`, `FeedbackOut`, `LastTrigger`) are intentionally ignored on load — don't "fix" old files that contain them.

Related: [[v2-core-architecture]]
