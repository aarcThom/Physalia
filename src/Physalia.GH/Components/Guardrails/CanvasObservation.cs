// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Grounding;
using Physalia.Core.Signals;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// On receiving a signal, scans Grasshopper components for runtime problems and routes the
/// outcome: a clean scan passes the incoming payload forward on the Success Signal; any errors,
/// warnings, or dead components (ones that produced no output) are gathered into a report routed
/// back on the Fail Signal.
///
/// <para>The scan is scoped by the GUIDs this component has been pointed at. A Component
/// Transmitter forwards the GUIDs of the components it placed (newline-separated) as its Success
/// payload — but on a patch turn that payload is only the DELTA (added and modified components),
/// while the patch's changes can break components placed in earlier turns. So CanvasObservation
/// ACCUMULATES every GUID that has ever arrived and scans the whole watched graph each turn,
/// pruning GUIDs whose components have since been removed. The watch list is session-only, like
/// all lifecycle state — the component reopens empty. When nothing is watched and the payload
/// carries no GUIDs it falls back to scanning the whole document (errors/warnings only),
/// preserving use as a standalone canvas probe. Wiring it after the Component Transmitter (and its
/// Fail Signal back through Feedback to the Conversation Log) turns the place → read → correct loop into a
/// visible cycle on the canvas.</para>
/// </summary>
public class CanvasObservation : RoutingComponentBase<string>
{
    // Every component GUID ever received on a consumed signal, minus those since removed from
    // the document (pruned at scan time). Session-only; never serialized.
    private readonly HashSet<Guid> _watchedGuids = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CanvasObservation"/> class.
    /// </summary>
    public CanvasObservation()
        : base("Canvas Observation", "Canvas Observation", "Scans the placed graph (or whole document) for errors, warnings, and dead components and routes a report back on the Fail Signal; a clean scan passes the signal through.", "Guardrails")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("F4B0D63C-8E57-4A4B-9C3D-5B2A7F1E04D8");

    /// <inheritdoc/>
    /// <remarks>
    /// The payload (the placed components' GUIDs from a Component Transmitter) is passed through
    /// unchanged when the scan is clean. A blank payload is accepted once GUIDs are being watched:
    /// a remove-only patch legitimately adds and modifies nothing, but the remaining watched graph
    /// still needs scanning.
    /// </remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload ?? string.Empty;
        return StringHelpers.IsNonBlank(data) || _watchedGuids.Count > 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Accumulates the incoming GUIDs into the watch list, so the read pass scans the whole
    /// LLM-built graph rather than just this turn's delta — a patch reports only its added and
    /// modified components, but its changes can break components placed in earlier turns.
    /// </remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        foreach (Guid guid in ParseGuids(data))
        {
            _watchedGuids.Add(guid);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Defers the scan until every scoped component has finished solving, so the runtime state
    /// read in <see cref="ReadSolve"/> reflects the settled graph rather than a still-expired one.
    /// </remarks>
    protected override bool IsReadReady(string data)
    {
        GH_Document? doc = OnPingDocument();
        if (doc is null)
        {
            return true;
        }

        foreach (IGH_DocumentObject obj in ScanScope(doc).Objects)
        {
            // A locked component never solves, so waiting on it would jam the settle gate.
            if (obj is IGH_ActiveObject { Locked: false } ao &&
                (ao.Phase == GH_SolutionPhase.Blank || ao.Phase == GH_SolutionPhase.Computing))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var dead = new List<string>();
        var signatures = new List<string>();
        var dataFlow = new List<string>();
        var signatureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool scopedScan = false;

        GH_Document? doc = OnPingDocument();
        if (doc is not null)
        {
            (IReadOnlyList<IGH_DocumentObject> scope, scopedScan) = ScanScope(doc);
            foreach (IGH_DocumentObject obj in scope)
            {
                if (obj is not IGH_ActiveObject ao)
                {
                    continue;
                }

                var objErrors = ao.RuntimeMessages(GH_RuntimeMessageLevel.Error);
                foreach (string message in objErrors)
                {
                    errors.Add($"{Label(ao)}: {message}");
                }

                var objWarnings = ao.RuntimeMessages(GH_RuntimeMessageLevel.Warning);
                foreach (string message in objWarnings)
                {
                    warnings.Add($"{Label(ao)}: {message}");
                }

                // GH runtime messages name the component but not the offending input (e.g. "Data
                // conversion failed from Number to Vector"), so pair every problem-reporting
                // component with its live input signature — the model then sees which slot expects
                // which type. Dead components are excluded: their problem is missing data, not types.
                if ((objErrors.Count > 0 || objWarnings.Count > 0) &&
                    obj is IGH_Component problemComp &&
                    signatureNames.Add(problemComp.Name))
                {
                    string inputs = string.Join(", ", ComponentSignatureProvider
                        .ReadPorts(problemComp.Params.Input)
                        .Select(p => SignatureFormat.Port(p.Name, p.TypeHint)));
                    if (!string.IsNullOrWhiteSpace(inputs))
                    {
                        signatures.Add($"{problemComp.Name} inputs: {inputs}");
                    }
                }

                // A component that produced no data on any output is "dead". VolatileData.IsEmpty
                // is the WRONG test: a solved output routinely holds a branch with zero items,
                // which reports non-empty — DataCount counts the items that actually exist. Skip a
                // component that already reported an error — its empty output is a cascade
                // symptom, not the root cause.
                bool isDead = objErrors.Count == 0 &&
                    obj is IGH_Component deadComp &&
                    deadComp.Params.Output.Count > 0 &&
                    deadComp.Params.Output.All(p => p.VolatileData.DataCount == 0);
                if (isDead)
                {
                    dead.Add(Label(obj));
                }

                // Problem and dead components additionally report their live data flow — how many
                // items each input collected and each output produced — so the model sees WHERE
                // data stops instead of blindly swapping construction strategies.
                if ((objErrors.Count > 0 || objWarnings.Count > 0 || isDead) && obj is IGH_Component flowComp)
                {
                    dataFlow.Add(FormatDataFlow(flowComp));
                }
            }
        }

        int total = errors.Count + warnings.Count + dead.Count;
        if (total == 0)
        {
            return RoutingResult.Ok(data);
        }

        // The last patch APPLIED, so the model's remembered base checksum is stale; carry the fresh
        // one in the feedback so the corrective patch cannot mismatch. Payload text only — carrier
        // discipline holds. IsReadReady settled the graph, so the export is stable here.
        string? checksum = doc is null ? null : GhJsonBridge.TryExportCanvasState(doc)?.Checksum;
        return RoutingResult.Fail(BuildFeedback(errors, warnings, dead, signatures, dataFlow, scopedScan, checksum), $"{total} problem(s) found in the scanned graph.", GH_RuntimeMessageLevel.Warning);
    }

    /// <summary>
    /// Renders a problem component's live data flow: items collected per input and items produced
    /// per output, e.g. <c>Boundary Surfaces 'Gable1' (guid): inputs [E=3] -> outputs [S=0]</c>.
    /// </summary>
    /// <param name="comp">The component to report.</param>
    /// <returns>The data-flow line.</returns>
    private static string FormatDataFlow(IGH_Component comp)
    {
        string ins = string.Join(", ", comp.Params.Input.Select(p => $"{PortLabel(p)}={p.VolatileData.DataCount}"));
        string outs = string.Join(", ", comp.Params.Output.Select(p => $"{PortLabel(p)}={p.VolatileData.DataCount}"));
        return comp.Params.Input.Count == 0
            ? $"{Label(comp)}: outputs [{outs}]"
            : $"{Label(comp)}: inputs [{ins}] -> outputs [{outs}]";
    }

    private static string PortLabel(IGH_Param param) =>
        string.IsNullOrWhiteSpace(param.NickName) ? param.Name ?? string.Empty : param.NickName;

    /// <summary>
    /// Labels a scanned object for the feedback report: name, nickname (when it differs from the
    /// name), and instanceGuid — the same identity the canvas-state grounding exports, so the
    /// model can address the exact component in a patch instead of guessing among same-named ones.
    /// </summary>
    /// <param name="obj">The scanned object.</param>
    /// <returns>The feedback label.</returns>
    private static string Label(IGH_DocumentObject obj)
    {
        return !string.IsNullOrWhiteSpace(obj.NickName) && !string.Equals(obj.NickName, obj.Name, StringComparison.Ordinal)
            ? $"{obj.Name} '{obj.NickName}' ({obj.InstanceGuid})"
            : $"{obj.Name} ({obj.InstanceGuid})";
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _watchedGuids.Clear();
    }

    /// <summary>
    /// The objects to scan: every watched component still alive on the document (the accumulated
    /// LLM-built graph), or every object on the document (except this CanvasObservation) when
    /// nothing is watched — the standalone-probe fallback.
    /// </summary>
    /// <param name="doc">The active document.</param>
    /// <returns>The objects in scope, and whether the scan is scoped to the watched graph.</returns>
    private (IReadOnlyList<IGH_DocumentObject> Objects, bool Scoped) ScanScope(GH_Document doc)
    {
        // ResolveWatchedObjects prunes the watch list, so a non-empty list after it means the
        // scan is scoped — even if every watched component is currently locked (excluded from
        // the resolved objects), the scope must not silently widen to the whole document.
        List<IGH_DocumentObject> watched = ResolveWatchedObjects(doc);
        return _watchedGuids.Count > 0
            ? (watched, true)
            : (doc.Objects.Where(o => o.InstanceGuid != InstanceGuid).ToList(), false);
    }

    /// <summary>
    /// Resolves the watch list against the live document, pruning GUIDs whose components have
    /// been removed (by a patch, an undo, or the user) so the list tracks the graph as it exists
    /// now. Locked components stay watched but are excluded from the scan — they never solve, so
    /// their state is stale and waiting on them would jam the settle gate.
    /// </summary>
    /// <param name="doc">The active document.</param>
    /// <returns>The live watched objects.</returns>
    private List<IGH_DocumentObject> ResolveWatchedObjects(GH_Document doc)
    {
        var resolved = new List<IGH_DocumentObject>();
        List<Guid>? stale = null;

        foreach (Guid guid in _watchedGuids)
        {
            if (doc.FindObject(guid, false) is IGH_DocumentObject obj)
            {
                if (obj is not IGH_ActiveObject { Locked: true })
                {
                    resolved.Add(obj);
                }
            }
            else
            {
                (stale ??= new List<Guid>()).Add(guid);
            }
        }

        if (stale is not null)
        {
            foreach (Guid guid in stale)
            {
                _watchedGuids.Remove(guid);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Parses newline-separated GUIDs from the payload, skipping any line that is not a GUID.
    /// </summary>
    /// <param name="payload">The incoming signal payload.</param>
    /// <returns>The GUIDs found in the payload.</returns>
    private static IEnumerable<Guid> ParseGuids(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            yield break;
        }

        foreach (string line in payload.Split('\n'))
        {
            if (Guid.TryParse(line.Trim(), out Guid guid))
            {
                yield return guid;
            }
        }
    }

    private static string BuildFeedback(IReadOnlyList<string> errors, IReadOnlyList<string> warnings, IReadOnlyList<string> dead, IReadOnlyList<string> signatures, IReadOnlyList<string> dataFlow, bool scopedScan, string? baseChecksum)
    {
        var sb = new StringBuilder();
        sb.AppendLine(scopedScan
            ? "The graph from your last response was placed on the canvas, and it reported the problems below. Each problem names its component by nickname and instanceGuid so you can address the exact component. Correct the definition and resubmit."
            : "The scanned document reported problems. Please correct the definition and resubmit.");

        AppendSection(sb, "Errors:", errors);
        AppendSection(sb, "Warnings:", warnings);
        AppendSection(sb, "Input signatures of the components that reported problems (match your data types to these):", signatures);
        AppendSection(sb, "Components that produced no output (check their inputs and upstream wiring):", dead);
        AppendSection(sb, "Data flow of the problem components (items collected per input -> items produced per output; an input at 0 received nothing from upstream):", dataFlow);

        if (!string.IsNullOrEmpty(baseChecksum))
        {
            sb.AppendLine();
            sb.AppendLine("Current base checksum — copy this verbatim into patch.base.checksum: " + baseChecksum);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder sb, string heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine(heading);
        foreach (string item in items)
        {
            sb.AppendLine($"  - {item}");
        }
    }
}
