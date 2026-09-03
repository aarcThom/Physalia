---
name: picker-ghjson-serialization
description: Picker selection persistence — the .ghjson extension, and the provisional-list trap that silently ate a restored pick
metadata: 
  node_type: memory
  type: project
  originSessionId: c4ef98d3-9f0b-4831-a5c4-409d6c488096
---

Picker selected value persistence (2026-06-27, builds clean, live test pending). Native `.gh`/`.ghx` files already persisted the Picker selection via `Picker.Write/Read("SelectedValue")` — that was never the gap. The gap was **GhJSON**: `GhJsonGrasshopper.GetByGuids` does NOT capture a component's native Write/Read blob, so `.ghjson` export/import (presets, Component Transmitter) lost the picker selection (the shipped presets show Pickers with only `componentState:{selected:true}`).

Fix mirrors the existing Feedback-links extension pattern in `GhJsonBridge.cs`: new key `physalia.pickerValue`. `InjectPickerValues(doc)` (called in `ExportToFile`) writes each live Picker's `SelectedValue` into `component.ComponentState.Extensions`. `RestorePickerValues(doc, result)` (called in `ExecutePut`, after `RestoreFeedbackLinks`) remaps id→guid via `PutResult.IdToGuidMapping`, finds the placed Picker, `SetSelectedValue` + `ExpireSolution`, and the caller re-solves if any restored. See [[ghjson-feedback-links]] for the identical mechanism.

**API-key safety (Thomas's explicit requirement):** the value stored is always a benign label — provider name (ApiKeys.Provider picker), file name (System Prompt preamble/schema picker), or model id. NEVER a secret. The actual API key flows as the label-only `GH_ApiKey` goo and is never serialized; ApiKeys.Provider is driven by the picker wire (not internalized). GhJSON DOES capture persistent param values as `internalizedData`, but no key is ever a param value. "Choice of which api key is fine to record" — satisfied.

Shipped presets (`Files/PRESETS/*.ghjson`) were exported before this change so carry no picker values; re-export to capture them.

## The provisional-list trap (2026-09-03) — why the Codex model always reopened as gpt-5.5

Saving was never broken here either. The pick was destroyed on RESTORE, by the Picker's own
fallback: `if (empty || !values.Contains(_selectedValue)) _selectedValue = values[0];`.

**A Picker always solves BEFORE the component it feeds** (it is upstream of it), so on the first
solve after a file opens, the source's list is whatever it holds at CONSTRUCTION. `CodexModel`
seeds `CodexConfig.KnownModels` = `{gpt-5.5, gpt-5.4, gpt-5.4-mini}` as a placeholder while the live
`model/list` fetch runs — so a saved `gpt-5.1-codex-max` was not in it, got snapped to `values[0]`,
and because the snap WRITES BACK the real list arriving a second later found nothing left to
restore. Claude Code was fine only because its list is a fixed complete set; Anthropic/Gemini/
OpenAI-compatible were fine only by accident, because theirs start EMPTY and an empty list hit an
early return that happened to skip the clobber.

Fix: `PickableInput` gained `bool IsSettled = true`. Snapping to `values[0]` now requires a settled
AND non-empty list. Every source whose real list arrives asynchronously reports `IsSettled: false`
until the fetch COMPLETES — success or failure, because a failed fetch means the seed/empty list is
now the best answer there is. An empty list never snaps either, so a saved pick survives an
unreachable provider offline. `Picker.MenuValues` prepends a currently-unoffered pick so it stays
visible and checked in both menus, and a genuine loss (settled list no longer offering it) now says
so with a Remark instead of switching models silently.

**The general lesson: never let a placeholder list overwrite persisted state.** `TokenEstimator`
had the identical bug via its `{"N/A"}` seed (a saved tiktoken encoding was reset every reopen) and
was fixed in the same pass. Anything new that seeds a non-empty list before knowing the truth must
declare it unsettled.
