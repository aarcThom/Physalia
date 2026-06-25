---
name: collapsed-chatbox-arrow
description: Collapsed Chatbox proxy shows a delegated bottom drag arrow when its harness holds exactly one transmitter
metadata: 
  node_type: memory
  type: project
  originSessionId: cff066f0-54e7-4263-940c-78ca23c8823e
---

A collapsed Chatbox harness proxy now draws the same bottom drag arrow as the transmitters, when the harness contains **exactly one** arrow-bearing member. Landed 2026-06-25, builds clean (`dotnet build src/Physalia.slnx -c Debug`), live-Rhino test pending.

Design: new `IHarnessArrow` interface (`src/Physalia.GH/Harness/IHarnessArrow.cs`) hides the two transmitters' different arrow models behind `ArrowGradient` / `GetArrowEndpoints(doc)` / `HandleDrop(doc, point, ctrl)`. Implemented explicitly on `PyTransmitter` (aqua→pink; drop links the script under the point via `GhPythonBridge.IsScriptComponent`, Ctrl unlinks) and `ComponentTransmitter` (orange→orchid; drop calls `SetPlacementTarget`). `Harness.TryGetSoleArrow(out IHarnessArrow?)` returns true only when one member implements it. `ChatboxAttrib` hosts the grip+wires (grip at bottom-centre of `_visualBounds`, pick region expanded +10px via `_gripBounds`) and forwards the drag to the real transmitter so the link/placement persists on expand.

Key choice vs the transmitters: the proxy grip hit zone is a **small box at the bottom-centre only** (`GripHitZone`), NOT the whole body — the transmitters' `GripLinkAttrib`/`CompTxAttrib` make the entire body a grip (`_gripBounds.Contains`), which would block dragging the Chatbox to move it. Considered (and rejected as too risky) a full shared `DragArrowGrip` refactor unifying the two transmitter state machines; chose delegation instead. See [[collapsible-harness]], [[component-transmitter]].
