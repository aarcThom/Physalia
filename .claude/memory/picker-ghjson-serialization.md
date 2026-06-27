---
name: picker-ghjson-serialization
description: Picker selected value now round-trips through .ghjson export/import via a component extension
metadata: 
  node_type: memory
  type: project
  originSessionId: c4ef98d3-9f0b-4831-a5c4-409d6c488096
---

Picker selected value persistence (2026-06-27, builds clean, live test pending). Native `.gh`/`.ghx` files already persisted the Picker selection via `Picker.Write/Read("SelectedValue")` — that was never the gap. The gap was **GhJSON**: `GhJsonGrasshopper.GetByGuids` does NOT capture a component's native Write/Read blob, so `.ghjson` export/import (presets, Component Transmitter) lost the picker selection (the shipped presets show Pickers with only `componentState:{selected:true}`).

Fix mirrors the existing Feedback-links extension pattern in `GhJsonBridge.cs`: new key `physalia.pickerValue`. `InjectPickerValues(doc)` (called in `ExportToFile`) writes each live Picker's `SelectedValue` into `component.ComponentState.Extensions`. `RestorePickerValues(doc, result)` (called in `ExecutePut`, after `RestoreFeedbackLinks`) remaps id→guid via `PutResult.IdToGuidMapping`, finds the placed Picker, `SetSelectedValue` + `ExpireSolution`, and the caller re-solves if any restored. See [[ghjson-feedback-links]] for the identical mechanism.

**API-key safety (Thomas's explicit requirement):** the value stored is always a benign label — provider name (ApiKeys.Provider picker), file name (Composer preamble/schema picker), or model id. NEVER a secret. The actual API key flows as the label-only `GH_ApiKey` goo and is never serialized; ApiKeys.Provider is driven by the picker wire (not internalized). GhJSON DOES capture persistent param values as `internalizedData`, but no key is ever a param value. "Choice of which api key is fine to record" — satisfied.

Shipped presets (`Files/PRESETS/*.ghjson`) were exported before this change so carry no picker values; re-export to capture them.
