---
name: rhinocommon-rag-tool
description: "RhinoCommon API search tool for code-gen grounding — reflection+XML merge index, search_rhinocommon tool node"
metadata: 
  node_type: memory
  type: project
  originSessionId: 84746927-a705-4772-8c7a-46abde294727
---

A model-invoked search tool over the RhinoCommon API, for grounding code generation (Python/C# script components). Goal is codegen, not user Q&A.

**Why reflection + XML, not XML alone:** the XML doc file only holds documented members and omits return type / static-ness / property types (the M: key encodes param types only). Reflection is the authoritative callable surface + exact signatures; XML supplies the prose. Merge by reconstructing the doc-comment ID (`M:Type.Method(paramTypeIds)`) from each reflected member.

**Built 2026-06-28 (builds clean):**
- `Physalia.Core/Api/ApiMember.cs` — record (Kind/DeclaringType/MemberName/Signature/IsStatic/Summary/Returns/Parameters) + cached lowercase search fields.
- `Physalia.Core/Api/ApiIndex.cs` — immutable members + pure code-aware keyword `Search` (exact name 1000 > prefix 200 > substring 80; type-tail + summary weak). No embeddings yet (hybrid/semantic is the planned upgrade if keyword falls short).
- `Physalia.GH/Generation/RhinoCommonIndexBuilder.cs` — reflects `typeof(Rhino.RhinoApp).Assembly` (RhinoCommon comes transitively via Grasshopper pkg), merges `RhinoCommon.xml` beside the dll (`Path.ChangeExtension(asm.Location,".xml")`), DeclaredOnly to avoid inherited noise, skips special-name. Cached via `Lazy`. Contains the doc-comment-ID generator (TypeDocId handles byref `@`/array/generic `{}`/method-`` `` ``/type-`` ` `` params).
- `Physalia.GH/Components/Tools/RhinoCommonSearch.cs` — `search_rhinocommon` tool node, `ToolComponentBase`, `RunsAsync=true` so the one-time index build runs off the solve thread (no GH freeze). No catalog wiring (RhinoCommon is intrinsic to host).

Mirrors the [[tools-in-use-component]] / ComponentSearch + Core/Catalog split. Wire Tool output → LLM Call.Tools; Result → Feedback → FeedbackCollector → Router.Results (standard tool wiring, see [[tool-calling-gh-loop]]). **Live Rhino test pending** (verify RhinoCommon.xml present beside the installed dll, build time, result quality).

**Mac Todo:** the XML resolution is already cross-platform — `LoadXmlDocs` derives the doc path from `assembly.Location` (sibling `.xml`), no hard-coded path, so no code change is needed for the dll/xml *location*. The unknown is whether Mac Rhino 8 actually *ships* `RhinoCommon.xml` beside the dll inside the `.app` bundle (`/Applications/Rhino 8.app/Contents/Frameworks/RhCore.framework/.../RhinoCommon.framework/.../RhinoCommon.dll`). Windows ships it at `C:\Program Files\Rhino 8\System\`. If absent on Mac, the index still builds (exact signatures, no prose); fallback = find the Mac doc XML or bundle a copy in `Files/`. Note is in a code comment in `RhinoCommonIndexBuilder.LoadXmlDocs`.
