---
name: ghjson-feedback-links
description: How Feedback→FeedbackCollector wireless links + a user comment round-trip through GhJSON export/import (extensions + IdToGuidMapping remap; no library change)
metadata: 
  node_type: memory
  type: project
  originSessionId: bbaabb4a-380c-4621-8961-67ab28dc45cd
---

Landed 2026-06-22 (branch feat/chat-window-menu). Two GhJSON serialization features, both entirely within the existing third-party GhJSON v1.0.0 NuGet API — the `ghjson-dotnet` library (architects-toolkit, cloned at `C:\Users\rober\repos\ghjson-dotnet`, local HEAD is v1.0.1) and `ghjson-spec` were **NOT modified**.

## Feedback → FeedbackCollector links survive export/import
The [[signal-lifecycle]] `Feedback` component (`Components/Regulators/Feedback.cs`) routes signals "wirelessly" to `FeedbackCollector`s by storing their `InstanceGuid`s in a private `List<Guid>` (`CollectorGuids` getter, `AddCollector(Guid)`) — there's NO GH wire, so `GhJsonGrasshopper.GetByGuids` (which only captures real param wires) misses the link entirely. Extra hurdle: `Put` runs `RegenerateInstanceGuids=true`, so any stored guid is stale on import.

Solution (all in `Generation/GhJsonBridge.cs`):
- **Export** (`InjectFeedbackLinks`, called from `ExportToFile` after `StripNickNames`): build `guidToId` from `doc.Components` (InstanceGuid→Id), resolve the live doc via `Grasshopper.Instances.ActiveCanvas?.Document`, and for each component whose live object `is Feedback`, map its `CollectorGuids` → GhJSON component **ids** (keeping only collectors also in the export — cross-boundary links are dropped) and store them in `component.ComponentState.Extensions["physalia.feedbackCollectors"] = new FeedbackLinkExtension { CollectorIds = ... }`. `extensions` is GhJSON's opaque pass-through dict (proven by the `gh.button` extension in `Files/PRESETS/complex-node.ghjson`), so it round-trips untouched.
- **Import** (`RestoreFeedbackLinks`, called from `PlaceDocument` after a successful `Put`): for each component with the extension, re-serialize the value with `JsonConvert` into the `FeedbackLinkExtension` DTO (dodges Newtonsoft `JObject`/`JArray` typing), then remap each id → new guid via **`PutResult.IdToGuidMapping : Dictionary<int,Guid>`** (verified present in the shipped v1.0.0 DLL), find the placed Feedback in `result.PlacedObjects`, and call `AddCollector(newGuid)`. Every lookup guarded. Works for ALL import paths (Deserializer, presets, ChatWindow "Add preset", CompTx) since they funnel through `PlaceDocument`. No re-solve needed (AddCollector just mutates the list; forwarding reads it at solve time).
- Relies on `GhJson.Fix` preserving existing ids (it only fills missing ones; ExportToFile files are always fully id'd via GetByGuids, so collectorIds==post-Fix ids==IdToGuidMapping keys).
- `private sealed class FeedbackLinkExtension { [JsonProperty("collectorIds")] List<int>? CollectorIds }`. Const key `FeedbackCollectorsExtensionKey = "physalia.feedbackCollectors"`.

## Optional top-of-file comment → metadata.description
`GhJsonDocument.Metadata` has a **private setter**, so a comment is injected by REBUILDING the doc: `new GhJsonDocument(doc.Schema, new GhJsonMetadata { Description = comment.Trim() }, doc.Components, doc.Connections, doc.Groups)` (component objects are mutable, so the feedback-link mutations carry over). Only done when comment is non-blank (else no metadata block). `ExportToFile(guids, path, string? comment = null)`.

`Serializer.cs` (Windows-only) gained an optional **"Comment"** text input at index 1 (Run stays 0 — no GUID/IO break), captured each `SolveInstance` into a `_comment` field (the interactive `CompleteSelection` runs from a canvas key handler, outside SolveInstance, so it can't read DA directly) and passed to `ExportToFile`.

## Gotchas
- `JsonException` is ambiguous in `GhJsonBridge.cs` (both `System.Text.Json` and `Newtonsoft.Json` are imported) — fully-qualify as `Newtonsoft.Json.JsonException`.
- `SerializePhySchema` (LLM-authored/CompTx authoring) was left unchanged; the import-side restore would still pick up the extension if such files ever carry it.
- Builds clean (`dotnet build src/Physalia.GH -p:BuildUI=false`, 0 errors). Live in-Rhino round-trip test still pending (place Feedback+2 collectors, link, serialize with comment, deserialize to clean canvas, confirm CollectorGuids.Count==2 and wireless forwarding works).
