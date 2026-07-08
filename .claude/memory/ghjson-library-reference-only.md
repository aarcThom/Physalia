---
name: ghjson-library-reference-only
description: ghjson-dotnet + ghjson-spec local repos are third-party reference downloads — never plan/make changes there; consume via nuget.org only (1.1.1+ has doc-level GhPatch + public canvas primitives)
metadata: 
  node_type: memory
  type: project
  originSessionId: 3b09d213-7ddf-48be-8581-8780d294732c
---

`C:\Users\rober\repos\ghjson-dotnet` and `C:\Users\rober\repos\ghjson-spec` are **third-party** (architects-toolkit org) repos Thomas downloaded **for reference only** — he does not own them and does not want them modified. Any GhJSON functionality must come from the published NuGet packages (GhJSON.Core / GhJSON.Grasshopper on nuget.org).

**Why:** On 2026-07-07 I wrongly planned a cross-repo workstream editing ghjson-dotnet; Thomas corrected: "I downloaded the ghjson source code for reference only. I don't want to modify it."

**JsonSchema.Net must stay in LOCKSTEP with GhJSON's own dependency** (7.2.2 for GhJSON 1.1.1). JsonSchema.Net 9.x REMOVED the 1-arg `JsonSchema.FromText(string)` (verified: 9.2.2 only has `FromText(string, BuildOptions, Uri, JsonDocumentOptions?)`); NuGet unifies to the highest version, so a higher direct reference in Physalia.Core makes GhJSON's `SchemaLoader` throw `MissingMethodException: FromText(System.String)` at RUNTIME in Rhino (surfaced 2026-07-07 via the canvas-state export → GetByGuids → document-build validation; GhJSON 1.0.0's validation had been failing SILENTLY under 9.2.2 for weeks — 1.1.1's DocumentBuilder made it a hard error). Physalia.Core.csproj carries a comment at the pin.

**The JSON stack must ship LOOSE, never ILRepack-merged** (JsonSchema.Net, Json.More, JsonPointer.Net, System.Text.Json, System.Text.Encodings.Web — RepackDenyList entries in Physalia.GH.csproj, 2026-07-07). JsonSchema.Net only ships a netstandard2.0 lib; its init-only property call sites into System.Text.Json carry the ns2.0 flavor's modreq identity (`[System.Text.Json]IsExternalInit` — the ns2.0 STJ embeds that type), while the deployed net7.0 STJ declares those accessors against `[System.Private.CoreLib]IsExternalInit`. Loose, this binds fine (identical to the xUnit configuration, which passes); merged+internalized, ILRepack's typeref rewrite yields `MissingMethodException: set_ObjectCreator` inside the .gha ONLY — i.e. a Rhino-only crash that unit tests can never reproduce. Diagnostic tell: 'works in tests, MissingMethodException in Rhino' on the JSON stack → suspect the merge.

**How to apply:** When Physalia needs GhJSON capability, check the latest published package version first (1.1.1 as of 2026-07-07: document-level GhPatch `GhJson.ApplyPatch/Diff/PatchFromJson/ValidatePatch` + public canvas primitives `Delete/Connect/Disconnect/CaptureExternalConnections/FindObject`, `PutOptions.RegenerateInstanceGuids=false`). Compose missing behavior in Physalia's `GhJsonBridge` from public APIs; internal library types (DocumentNormalizer checksum, ObjectHandlerOrchestrator, CanvasConnector) are off-limits. Known 1.1.1 bug to work around in Physalia: `PatchApplier` renumbers colliding added-component ids without remapping their connections/group members — pre-remap ids before calling ApplyPatch. Related: [[component-transmitter]].
