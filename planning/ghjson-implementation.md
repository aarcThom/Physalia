# GhJSON Implementation Guide (Physalia)

> Self-reference guide for implementing Grasshopper export → `.json` and `.json` → canvas import in **Physalia.GH** using the **ghjson-dotnet** library (v1.0.0). API signatures below were cross-checked against the library source, not assumed.

---

## 1. Overview & references

**GhJSON** is an open JSON format that represents a Grasshopper definition (components, wires, groups, slider/panel/script state) as a single document. It is published by **Architect's Toolkit**; the .NET implementation is `ghjson-dotnet`. Physalia adopted it (commit `f76f7d4`) after abandoning the custom `AssemblyDefinition`/`AssemblyIO` subsystem.

Local repos on this machine:
- Spec + examples: `C:\Users\rober\repos\ghjson-spec`
  - `docs/specification.md` — full format spec (v1.0)
  - `docs/extensions.md` — special-component extensions (slider, panel, scripts, scribble…)
  - `docs/ghpatch.md` — diff/patch format (not needed for basic round-trip)
  - `examples/simple-addition.ghjson` — minimal example (two sliders → Add → Panel)
- Library source: `C:\Users\rober\repos\ghjson-dotnet`
- JSON Schema URL: `https://architects-toolkit.github.io/ghjson-spec/schema/v1.0/ghjson.schema.json`

Packages live in the NuGet cache (`~/.nuget/packages/ghjson.core`, `ghjson.grasshopper`), both **v1.0.0**, but are **not yet referenced** in any `.csproj` — that is Step 1.

---

## 2. Step 1 — Wire up the NuGet packages

Add to `src/Physalia.GH/Physalia.GH.csproj`, inside the existing package `<ItemGroup>` (currently lines 44–47, next to `Grasshopper`/`System.Drawing.Common`):

```xml
<PackageReference Include="GhJSON.Core" Version="1.0.0" />
<PackageReference Include="GhJSON.Grasshopper" Version="1.0.0" />
```

Notes:
- **Do not** add `ExcludeAssets="runtime"` (unlike the `Grasshopper` reference). The `Grasshopper`/`RhinoCommon` assemblies are provided by the Rhino host at runtime, but the GhJSON DLLs are **not** — with `EnableDynamicLoading=true` + `.gha` output they must ship next to the compiled `.gha`. Leave `Private` at its default (`true`).
- `GhJSON.Grasshopper` transitively pulls `GhJSON.Core` and `Newtonsoft.Json` 13.0.3. That Newtonsoft version matches what Rhino 8 already loads, so no binding conflict is expected.
- TFMs: `GhJSON.Core` = netstandard2.0; `GhJSON.Grasshopper` ships both `net7.0` and `net7.0-windows7.0`. Both Physalia.GH TFMs (`net7.0-windows` on Win, `net7.0` on Mac) are covered — **no Mac-specific `HintPath` is required** (unlike the `Rhino.Runtime.Code` references).

Sanity check after adding: `dotnet restore` then `dotnet build src/Physalia.GH` should resolve cleanly.

---

## 3. API surface (verified against ghjson-dotnet v1.0.0)

### Core facade — `GhJSON.Core.GhJson` (static)
```csharp
GhJsonDocument   FromFile(string path);                              // File.ReadAllText -> FromJson
GhJsonDocument   FromJson(string json);
GhJsonDocument   FromStream(Stream stream);
void             ToFile(GhJsonDocument doc, string path, WriteOptions? = null);
string           ToJson(GhJsonDocument doc, WriteOptions? = null);   // WriteOptions { bool Indented; bool IncludeNullValues; }
ValidationResult Validate(GhJsonDocument doc, ValidationLevel = Standard);
ValidationResult Validate(string json, ValidationLevel = Standard);
bool             IsValid(string json, out string? message, ValidationLevel = Standard);
FixResult        Fix(GhJsonDocument doc, FixOptions? = null);        // assign/regenerate ids & instance guids
```

### Grasshopper facade — `GhJSON.Grasshopper.GhJsonGrasshopper` (static)
```csharp
// EXPORT (GH -> GhJSON)
GhJsonDocument Get(GetOptions? = null);              // reads Instances.ActiveCanvas.Document — ALL objects
GhJsonDocument GetSelected();                        // == Get(new GetOptions { SelectedOnly = true })  <-- Physalia export uses this
GhJsonDocument GetByGuids(IEnumerable<Guid> guids);
GhJsonDocument Serialize(IEnumerable<IGH_DocumentObject> objects, SerializationOptions? = null); // canvas-independent escape hatch

// IMPORT (GhJSON -> GH)
PutResult             Put(GhJsonDocument doc, PutOptions? = null);                       // PLACES onto Instances.ActiveCanvas.Document
DeserializationResult Deserialize(GhJsonDocument doc, DeserializationOptions? = null);   // builds objects WITHOUT placing
```

### Options & results
- `GetOptions { SelectedOnly; IncludeConnections=true; IncludeGroups=true; IncludeInternalizedData=true; IncludeRuntimeMessages=false; IncludeMetadata=false; Metadata* overrides }`
- `PutOptions { PointF Offset; CreateConnections=true; CreateGroups=true; SelectPlacedObjects=true; RegenerateInstanceGuids=true; SkipInvalidComponents=true; AutoOffset=true; AutoOffsetSpacing=100f }`
- `PutResult { bool Success; int ComponentsPlaced; int ConnectionsCreated; int GroupsCreated; List<IGH_DocumentObject> PlacedObjects; Dictionary<int,Guid> IdToGuidMapping; List<string> FailedComponents; List<string> Warnings; string? ErrorMessage }`

### Key namespaces / usings
```csharp
using GhJSON.Core;                          // GhJson facade, WriteOptions, ValidationResult
using GhJSON.Core.SchemaModels;             // GhJsonDocument, GhJsonComponent, GhJsonConnection, GhJsonGroup, GhJsonMetadata
using GhJSON.Grasshopper;                    // GhJsonGrasshopper facade
using GhJSON.Grasshopper.GetOperations;      // GetOptions
using GhJSON.Grasshopper.PutOperations;      // PutOptions, PutResult
```

### ⚠️ Critical hazards
1. **`Get`/`GetSelected`/`Put` target `Instances.ActiveCanvas.Document`**, not the component's own document. Normally identical, but if you ever need a specific document use `Serialize(objects)` (export) or `Deserialize` + manual placement (import).
2. **`Put()` mutates the live document** — it adds objects and expires solutions. Calling it directly inside `SolveInstance` is unsafe (mutating the document mid-solve). **Defer it** (see Component 3).
3. **`AddRuntimeMessage` only works on the main thread during `SolveInstance`** (existing Physalia rule). Capture any deferred result/warning into a field, then emit it on the next solve.

---

## 4. GhJSON format primer

Root document shape:
```json
{
  "schema": "1.0",
  "metadata": { },          // optional
  "components": [ ],        // required
  "connections": [ ],      // optional
  "groups": [ ]            // optional
}
```
- **components[]** — each has `name`, `library`, `nickName`, `componentGuid`, `instanceGuid`, an integer `id` (used for cross-references), `pivot` (canvas position), `inputSettings`/`outputSettings`, and a `componentState.extensions` dict for special components.
- **connections[]** — `{ from: { id, paramName|paramIndex }, to: { id, paramName|paramIndex } }`.
- **groups[]** — `{ instanceGuid, id, name, color, members: [componentId…] }`.
- **Special components** via `componentState.extensions` keys: `gh.numberslider` (`value: "5<0~10>"`), `gh.panel`, `gh.valuelist`, `gh.csharp`/`gh.python`/`gh.ironpython`, `gh.scribble`, etc.
- **Data types** are prefixed strings: `pointXYZ:1,2,3`, `number:3.14`, `interval:0<10`, `argb:255,128,64,32`.

See `ghjson-spec/examples/simple-addition.ghjson` for a complete minimal file. For the components below you generally don't touch the schema models directly — the facade handles serialization both ways.

---

## 5. Component 1 — Export ("Selected only")

Disassembler-style: serialize the **currently-selected** canvas objects, emit the JSON string, and optionally write it to disk.

- **Location:** `src/Physalia.GH/Components/Assemblers/GhJsonExporter.cs` (create the `Assemblers` folder, or reuse `Utility/`).
- **Base:** `PhyBase` → `base("GhJSON Exporter", "GhExp", "Serialises the selected Grasshopper objects to GhJSON.", "Assemblers")` with a fresh `ComponentGuid`.
- **Inputs** (Title Case, per project convention):
  - `Run` — bool, item
  - `File Path` — string, item, **optional** (`pManager[i].Optional = true`)
- **Outputs:**
  - `JSON` — string, item
  - `Path` — string, item (echo of the written path)
- **`SolveInstance` body:**
  ```csharp
  bool run = false;
  string path = string.Empty;
  if (!DA.GetData(0, ref run) || !run) return;
  DA.GetData(1, ref path);

  GhJsonDocument doc = GhJsonGrasshopper.GetSelected();
  if (doc.Components is null || doc.Components.Count == 0)
  {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No objects selected on the canvas.");
      return;
  }

  string json = GhJson.ToJson(doc, new WriteOptions { Indented = true });
  DA.SetData(0, json);

  if (!string.IsNullOrWhiteSpace(path))
  {
      GhJson.ToFile(doc, path);   // == File.WriteAllText(path, json)
      DA.SetData(1, path);
  }
  ```
- **Notes:** `GetSelected()` reads `Instances.ActiveCanvas.Document` — the component exports whatever is selected on the *active* canvas at solve time. Prefer a plain path-string input over a WinForms `SaveFileDialog` to stay Mac-compatible (the old `Disassembler` Mac caveat in MEMORY.md).

Reference template for structure: `src/Physalia.GH/Components/Utility/InstructionsCompositor.cs`.

---

## 6. The `GhJsonDocument` goo + param (shared by Components 2 & 3)

To pass a parsed document between the loader and placer, wrap it in a goo, following the existing `GH_Conversation` / `Param_Conversation` pattern.

**`src/Physalia.GH/Goo/GH_GhJsonDocument.cs`:**
```csharp
// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using GhJSON.Core.SchemaModels;
using Grasshopper.Kernel.Types;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapper for a <see cref="GhJsonDocument"/>.
/// </summary>
public class GH_GhJsonDocument : GH_Goo<GhJsonDocument>
{
    public GH_GhJsonDocument() { }

    public GH_GhJsonDocument(GhJsonDocument document) => Value = document;

    public override bool IsValid => Value is not null;
    public override string TypeName => "GhJSON Document";
    public override string TypeDescription => "A parsed GhJSON Grasshopper definition.";
    public override IGH_Goo Duplicate() => new GH_GhJsonDocument(Value);
    public override string ToString() =>
        Value is null ? string.Empty : $"GhJSON: {Value.Components?.Count ?? 0} components";
}
```

**`src/Physalia.GH/Parameters/Param_GhJsonDocument.cs`:**
```csharp
// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_GhJsonDocument"/> values.
/// </summary>
public class Param_GhJsonDocument : GH_Param<GH_GhJsonDocument>
{
    public Param_GhJsonDocument()
        : base("GhJSON Document", "GhDoc", "A parsed GhJSON Grasshopper definition.", "Physalia", "Params", GH_ParamAccess.item)
    {
    }

    public override Guid ComponentGuid => new Guid("<GENERATE-NEW-GUID>");
    public override GH_Exposure Exposure => GH_Exposure.hidden;
}
```

---

## 7. Component 2 — Import loader / validator (Transmitter-side)

Parses a `.json` file (or raw JSON), validates it, and outputs the parsed document goo plus diagnostics. **No canvas mutation** — safe to run every solve.

- **Location:** `src/Physalia.GH/Components/Assemblers/GhJsonLoader.cs`
- **Base:** `PhyBase` → `base("GhJSON Loader", "GhLoad", "Loads and validates a GhJSON file.", "Assemblers")`.
- **Inputs:**
  - `File Path` — string, item, optional
  - `JSON` — string, item, optional (use whichever is provided; prefer `File Path` if both)
- **Outputs:**
  - `Document` — `Param_GhJsonDocument`
  - `Valid` — bool, item
  - `Component Count` — int, item
  - `Message` — string, item
- **`SolveInstance` body:**
  ```csharp
  string path = string.Empty, jsonIn = string.Empty;
  DA.GetData(0, ref path);
  DA.GetData(1, ref jsonIn);

  string json;
  try
  {
      json = !string.IsNullOrWhiteSpace(path) ? System.IO.File.ReadAllText(path) : jsonIn;
  }
  catch (Exception ex)
  {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Could not read file: {ex.Message}");
      return;
  }
  if (string.IsNullOrWhiteSpace(json)) return;   // nothing supplied yet

  bool valid = GhJson.IsValid(json, out string? message);
  GhJsonDocument doc = GhJson.FromJson(json);

  DA.SetData(0, new GH_GhJsonDocument(doc));
  DA.SetData(1, valid);
  DA.SetData(2, doc.Components?.Count ?? 0);
  DA.SetData(3, message ?? "Valid");
  if (!valid)
  {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message ?? "Document failed validation.");
  }
  ```
- **Optional:** call `GhJson.Fix(doc)` to auto-assign missing ids / instance guids before handing off to the placer.

---

## 8. Component 3 — Import placer (Receiver-side)

Takes the parsed document and places it onto the canvas via `Put()`. **This is the side-effecting component** — it must defer placement off the solve thread.

- **Location:** `src/Physalia.GH/Components/Assemblers/GhJsonPlacer.cs`
- **Base:** `PhyBase` → `base("GhJSON Placer", "GhPlace", "Places a loaded GhJSON document onto the canvas.", "Assemblers")`.
- **Inputs:**
  - `Document` — `Param_GhJsonDocument`
  - `Run` — bool, item
  - `Regenerate GUIDs` — bool, item, default `true`
  - `Offset` — number/point, item, optional
- **Outputs:**
  - `Success` — bool, item
  - `Placed` — int, item
  - `Warnings` — string, list
- **Pattern — defer the mutation.** `Put()` adds objects to the live document, so schedule it rather than calling it in `SolveInstance`. Surface the `PutResult` via a field emitted on the next solve (the existing `_fetchWarning` convention):
  ```csharp
  private PutResult? _lastResult;
  private bool _scheduled;

  protected override void SolveInstance(IGH_DataAccess DA)
  {
      // Emit results captured from the previous deferred placement
      if (_lastResult is not null)
      {
          DA.SetData(0, _lastResult.Success);
          DA.SetData(1, _lastResult.ComponentsPlaced);
          DA.SetDataList(2, _lastResult.Warnings);
          if (!_lastResult.Success && _lastResult.ErrorMessage is not null)
              AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastResult.ErrorMessage);
          _lastResult = null;
          return;
      }

      var docGoo = new GH_GhJsonDocument();
      bool run = false, regen = true;
      if (!DA.GetData(0, ref docGoo) || !DA.GetData(1, ref run) || !run) return;
      DA.GetData(2, ref regen);

      if (docGoo.Value is not GhJsonDocument doc) return;
      if (_scheduled) return;

      var opts = new PutOptions
      {
          RegenerateInstanceGuids = regen,
          CreateConnections = true,
          CreateGroups = true,
          AutoOffset = true,
      };

      var ghDoc = OnPingDocument();
      _scheduled = true;
      ghDoc?.ScheduleSolution(5, _ =>
      {
          _lastResult = GhJsonGrasshopper.Put(doc, opts);   // runs after this solve completes
          _scheduled = false;
          ExpireSolution(false);   // trigger the result-emitting solve above
      });
  }
  ```
- **Hazard recap:** `Put()` targets `Instances.ActiveCanvas.Document`; when scheduled via `OnPingDocument().ScheduleSolution`, that callback runs against the same document safely after the current solve. Never call `Put()` synchronously inside `SolveInstance`.

---

## 9. Cross-platform / build notes

- Both GhJSON package TFMs cover `net7.0` and `net7.0-windows7.0`, so **no Mac-specific `HintPath` ItemGroups** are needed (contrast the `Rhino.Runtime.Code` / `RhinoCodePlatform.*` references which are hand-pathed per OS).
- Keep all three components **WinForms-free** — use plain `string` path inputs, not `SaveFileDialog`/`OpenFileDialog` — so they compile on the Mac `net7.0` TFM (the lesson from the old `Disassembler`, recorded in MEMORY.md "Mac Todo").
- Register the new params (`Param_GhJsonDocument`) and components in the GH category alongside existing ones; give every new `ComponentGuid` / `Param.ComponentGuid` a freshly generated GUID.

---

## 10. Round-trip summary

| Direction | Calls |
|---|---|
| **Export** | `GhJsonGrasshopper.GetSelected()` → `GhJson.ToJson(doc, opts)` (+ `GhJson.ToFile`) |
| **Load** | `File.ReadAllText` → `GhJson.IsValid` / `GhJson.FromJson` → `GH_GhJsonDocument` goo |
| **Place** | (deferred) `GhJsonGrasshopper.Put(doc, PutOptions)` → `PutResult` |
