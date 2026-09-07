---
name: session-budget-and-records
description: "2026-09-06 — Budget Guard bounds a SESSION's spend (nothing else did), the conversation autosaves to the project folder and resumes only on request, and runs.jsonl logs one line per inference call."
metadata: 
  node_type: memory
  type: project
  originSessionId: 87baa99a-bc2a-4051-b3ac-90fef7c24c3a
  modified: 2026-09-07T04:17:27.918Z
---

Three things a pipeline needs once it can start rounds on its own: a bound on what it may spend, a
memory that survives a restart, and a record of what it actually did. BUILT 2026-09-06.
**The autosave and the run log are VERIFIED ON DISK** from a live Rhino run — a Budget Guard with
caps also solved and passed a signal through. The extension card, the resume button and the state
tool have still not been exercised.

**Why:** Signal Limiter caps one loop's rounds and Stall Guard catches a loop repeating itself, but
neither bounds a SESSION — and until [[trigger-tier]] existed, a session was bounded by a person
being present. **A pipeline with any trigger armed and no Budget Guard has no upper bound on its
bill.** Separately, nothing in the lifecycle persisted (correct for signals, wrong for the
transcript): reopening a file to find the model had forgotten the whole brief was the most expensive
thing about the memory model.

**How to apply:**
- **`SpendPolicy` (Core, pure, tested) + `SpendLedger` (per LOCAL document, session-only).** The LLM
  Call records, the Budget Guard reads, **no wire between them** — the same document-keyed pairing as
  the PDF registry and [[harness-delegation]].
- **Checked BEFORE a call, against what is already spent**, so a pipeline can overrun by up to one
  call. The cost of a call is not knowable until it is made, and a runaway loop is stopped just as
  dead one call late. Do not "improve" this by estimating the next call.
- **A call with no usage reported still counts as a call.** A CLI provider on a subscription reports
  no token figure at all, so a token cap alone cannot bound it — that is why `Max Calls` exists next
  to tokens rather than instead of them. Cached tokens are added back into the total (reporting
  `InputTokens` alone makes a cache hit look like the prompt shrank).
- **Over budget it reuses `ToolApprovalBroker`** rather than growing a fourth broker: the question
  genuinely IS an approval — may this spend more of your money — and every edge failing closed is
  exactly what a budget wants. `Extension = 0` makes the budget final and the card never appears.
  "Reset Budget" is the menu verb; `Clear Outputs` deliberately does NOT reset the tally.
- **The transcript lives in the PROJECT FOLDER, not the `.gh`**: `conversation.json` +
  `conversation-images/`. It is project material, so it ships in a `.phy`, and a `.gh` gets copied and
  emailed far more casually. Images go BESIDE the JSON (base64 triples the bytes and makes a file
  nobody can read by eye) and are written ONCE, since keys are derived from position in an append-only
  history — that is what makes a per-turn autosave cheap.
- **Autosave always; resume only on request.** Auto-loading would hand a shared pipeline its author's
  conversation, paid for on the next call. The offer is a button in the chat window's empty state
  (`resumeTurns` on `UiState` → `phbridge://resume`), shown only while the conversation is empty,
  which is also the only time resuming has an unambiguous meaning. This replaced two
  "not yet implemented" menu stubs on the Conversation Log.
- `ConversationArchiver` reading rules: a NEWER format version is **refused** (not guessed at), an
  unknown block type is **dropped with the turn kept**, a missing image file costs its block and not
  the brief, a turn left with no blocks is skipped (a provider rejects an empty turn), and
  consecutive same-role turns are **merged** rather than refused — somebody's transcript beats no
  transcript.
- **`runs.jsonl` is JSONL because a log is a STREAM**: an append needs no read first and a truncated
  last line costs one record. That is the opposite of `downloads.json`, which is a LEDGER read whole
  to answer "is this file accounted for". Per CALL, not per round — a round is a boundary nobody
  would agree on, a call is what costs money. Failures are logged too, and **a write failure is
  swallowed on purpose**: a log that cannot be written must not cost the answer it was recording.
- **Pipeline State** (`state` tool + `StateStore`) is the session-only, per-harness board the GRAPH
  can branch on — not the Memory tool, which is prose files the pipeline never reads. Capped at 64
  keys / 8KB, and a full board REFUSES a new key rather than evicting one (a gate may be watching the
  one that would have gone).
- **Undo Last Placement** on the Component Transmitter removes what the last placement ADDED and says
  so — a ghpatch also modifies existing components and nothing recorded their prior state.

## Verified live 2026-09-06, and one thing it changed my mind about

After two rounds in a harness named `watch-and-repeat`,
`Files/PROJECT_FILES/watch-and-repeat/` (under `bin`, beside the `.gha` — NOT the repo `Files/`)
held exactly what it should:

- `conversation.json`, 12KB, `{"version": 1, "saved": ..., "turns": [...]}` with text blocks — the
  per-turn autosave works.
- `runs.jsonl`, one line per call:
  `{"when":"...","harness":"watch-and-repeat","model":"sonnet","inputTokens":6,"outputTokens":1925,"ms":24993,"ok":true}`

**`inputTokens: 6` on a 5,650-character system prompt.** That is not a bug — a CLI provider holds
the context itself, so what it reports is the DELTA it was sent this turn, not the size of the
prompt. This sharpens the "a call with no usage still counts as a call" rule into something stronger:
**on a CLI provider a token cap is not merely incomplete, it is MEANINGLESS** — the numbers are real
but they measure a different thing, and 300k tokens would never be reached however long the session
ran. `Max Calls` is the only cap that bounds a Claude Code or Codex pipeline, and a harness built on
one should set it and leave Max Tokens at zero rather than setting both.

Related: [[trigger-tier]], [[harness-names-and-phy-packages]], [[project-file-tools]],
[[token-count-human-tool]], [[watch-modelling]], [[claudecode-warm-process]].
