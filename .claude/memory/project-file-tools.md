---
name: project-file-tools
description: "2026-09-05 — Project Folder grounder plus download_file and read_file tools; the approval seam; why read_file's guard is not a sandbox"
metadata: 
  node_type: memory
  type: project
  originSessionId: 41930f90-4aec-4ebd-8c9e-359924617843
  modified: 2026-09-06T05:31:51.198Z
---

Built 2026-09-05, for the case that prompted it: a Vancouver open-data `.las` that can be downloaded
but not reached through the API. Three components — **Project Folder** (grounding), **Download File**
and **Read File** — plus `IToolApprover`/`RhinoToolApprover`. Detail in CLAUDE.md.

**Why:** the model needed a file on disk AND the definition needed the path. Reading a 400MB point
cloud into a conversation is useless; importing it is the point.

**How to apply:**
- **The path on the wire is the deliverable.** `Downloaded Files` carries absolute paths, the same
  two-ways rule as API Call: summary to the model, data to the canvas. The grounding puts the
  ABSOLUTE folder path in the prompt so `run_rhino_script` can `open` what it names — that is the
  join between the two halves.
- Guards are on the DESTINATION, not the URL: `read_url` already lets the model fetch anything, so
  reach is not new; writing bytes is. Byte budget enforced while STREAMING (Content-Length is a
  claim), temp-file-then-move, http(s) re-checked on the FINAL address after redirects.
- **Sniff the bytes.** An open-data portal answering a missing tile with a 200 and an HTML error page
  is indistinguishable from success by every other measure. `FileSniff` catches it.
- **Say plainly that `read_file`'s containment is not a sandbox.** `run_rhino_script` runs
  unrestricted Python in-process (and reaches `System.IO` through `clr` regardless of any `open`
  shim), so where both are advertised the model already has the disk. The guard catches accidents and
  bounds cost. Documenting it as security would make someone rely on it.
- Restricting `run_rhino_script` by path is not achievable; the honest options are don't-advertise-it,
  approve the SCRIPT (the seam is built for this, not yet wired to that node), or a declared allowlist
  in the `GroundingDirective` framed as guidance.
- **The approval seam fails closed on every edge** and waits five minutes (MCP sign-in's reasoning).
  It is a CARD in the chat window (`ToolApprovalBroker` + `ApprovalCard.svelte`), pushed the instant
  the model asks via a `Changed` event rather than on the window tick. **No chat window open denies
  immediately** rather than burning the timeout. Rendered outside the `staticSurface` guard so it is
  visible from Home and the setup pages; only the oldest of a queue shows, because stacked consent
  prompts get cleared unread. Headless-verified (render, verbatim detail, allow/deny navigation,
  double-click guard, queueing).
- A node with no project folder advertises NOTHING — the API Call ruling: a tool that fails every
  call reads as a broken capability rather than an unconfigured node.

Core verified by tests (847 pass); nothing run in Rhino. The three components have no icons yet and
fall back to `brain.png`; prompts are in `planning/component-icon-prompts.md`.
Related: [[harness-names-and-phy-packages]], [[api-call-tool]], [[run-rhino-script-tool]].
