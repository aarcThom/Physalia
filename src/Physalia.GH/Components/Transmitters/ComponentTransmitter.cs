// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Signals;
using Physalia.Core.Validation;
using Physalia.GH.Attributes;
using Physalia.GH.Generation;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// Takes an LLM-generated document (arriving as the consumed signal's payload) and applies it to
/// the canvas, then routes the outcome. Two modes, detected from the payload itself:
/// a <b>full GhJSON graph</b> is placed to the right of this component, with the previous
/// placement removed first (regenerate-from-scratch semantics — presets and first generations);
/// a <b>ghpatch</b> (<c>"kind": "ghpatch"</c>) edits the existing canvas IN PLACE — components are
/// added, modified, removed, and rewired without disturbing anything else, and nothing is deleted
/// wholesale, so the user can iterate on their definition with the model turn after turn. This
/// component reports only <b>mechanical</b> problems — invalid input, a failed placement, patch
/// operations that did not apply — back on the Fail Signal so the model can fix and resubmit. A
/// clean outcome routes the affected components' GUIDs forward on the Success Signal; a Canvas
/// Observation wired downstream scopes its runtime-health scan (errors, warnings, dead
/// components) to exactly those GUIDs.
/// </summary>
public class ComponentTransmitter : RoutingComponentBase<string>, IHarnessArrow
{
    private const float PlacementGap = 50f;

    private readonly List<Guid> _placedGuids = new();
    private string _pendingJson = string.Empty;
    private bool _pendingIsPatch;
    private CanvasPatchOutcome? _patchOutcome;
    private bool _lenientBase;
    private string? _pushError;
    private IReadOnlyList<string> _placeWarnings = Array.Empty<string>();
    private IReadOnlyList<string> _unfixedIssues = Array.Empty<string>();

    // Drop-arrow placement origin, stored as an offset from this component's pivot so the
    // arrow tip travels with the component; null falls back to placement right of the node.
    private PointF? _placementOffset;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentTransmitter"/> class.
    /// </summary>
    public ComponentTransmitter()
        : base(
            "Component Transmitter",
            "CompTx",
            "Places an LLM-generated GhJSON graph on the canvas. Clean placement routes the placed components' GUIDs forward (for a Canvas Observation to scan); mechanical placement problems route a description back.",
            "Transmitters")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("4BA76257-AD4C-462C-AB7E-B130DB176BF4");

    /// <summary>
    /// Gets the absolute canvas point (the drop-arrow tip) where the placed graph's top-left
    /// origin lands, or <see langword="null"/> when no arrow has been dropped. Reconstructed
    /// each call from the stored pivot-relative offset so it follows the moving component.
    /// </summary>
    public PointF? PlacementTarget =>
        _placementOffset is { } off
            ? new PointF(Attributes.Pivot.X + off.X, Attributes.Pivot.Y + off.Y)
            : null;

    /// <inheritdoc/>
    public override void CreateAttributes()
    {
        m_attributes = new CompTxAttrib(this);
    }

    /// <summary>
    /// Stores the placement-target arrow tip, dropped anywhere on the canvas, as an offset from
    /// the component's pivot. Called by <see cref="CompTxAttrib"/> when the user drops the arrow.
    /// </summary>
    /// <param name="canvasPoint">The drop point in canvas coordinates.</param>
    public void SetPlacementTarget(PointF canvasPoint)
    {
        PointF pivot = Attributes.Pivot;
        _placementOffset = new PointF(canvasPoint.X - pivot.X, canvasPoint.Y - pivot.Y);
    }

    /// <summary>
    /// Clears the placement target, reverting to the default placement right of the component.
    /// </summary>
    public void ResetPlacementTarget()
    {
        _placementOffset = null;
    }

    // IHarnessArrow — lets a collapsed Chat proxy delegate its bottom arrow to this transmitter.
    // The wire lands on the stored placement point; a drop simply stores the new point.

    /// <inheritdoc/>
    IEnumerable<PointF> IHarnessArrow.GetArrowEndpoints(GH_Document doc)
    {
        if (PlacementTarget is { } target)
        {
            yield return target;
        }
    }

    /// <inheritdoc/>
    void IHarnessArrow.HandleDrop(GH_Document doc, PointF dropPoint, bool ctrl)
    {
        SetPlacementTarget(dropPoint);
        ExpireSolution(true);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Placement is deferred to <c>RhinoApp.Idle</c> (it mutates the document and triggers its
    /// own solution, so it cannot run inside <c>SolveInstance</c>), so the read pass is
    /// scheduled by this component rather than auto-scheduled by the base.
    /// </remarks>
    protected override bool AutoScheduleRead => false;

    /// <inheritdoc/>
    /// <remarks>
    /// The GhJSON graph arrives as the consumed signal's payload.
    /// </remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload;
        return StringHelpers.IsNonBlank(data);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Validates the GhJSON on the solve thread, then queues the (document-mutating) placement
    /// to run outside the current solution. Validation failures are stashed and surfaced in
    /// <see cref="ReadSolve"/>.
    /// </remarks>
    protected override void PushSolve(string data, IGH_DataAccess da)
    {
        _pushError = null;
        _patchOutcome = null;
        _pendingIsPatch = GhPatchDetector.IsGhPatch(data);

        if (_pendingIsPatch)
        {
            if (!GhJsonBridge.IsPatchJson(data, out string? patchMessage))
            {
                _pushError = $"Not a valid ghpatch: {patchMessage}";
                RequestReadPass();
                return;
            }
        }
        else if (!GhJsonBridge.IsValidJson(data, out string? message))
        {
            _pushError = $"Not valid GhJSON: {message}";
            RequestReadPass();
            return;
        }

        _pendingJson = data;

        // Placement/patching adds and mutates objects and triggers its own NewSolution, so it must
        // run outside the current solution. RhinoApp.Idle fires on the UI thread once the solution
        // has settled.
        Rhino.RhinoApp.Idle += OnIdlePlace;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reports only mechanical placement outcomes (known as soon as placement finishes), so it
    /// need not wait for the placed components to solve — a Canvas Observation wired downstream owns the
    /// runtime-health gate and scan.
    /// </remarks>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        if (_pushError != null)
        {
            return RoutingResult.Fail(_pushError, _pushError, GH_RuntimeMessageLevel.Error);
        }

        if (_pendingIsPatch)
        {
            return ReadPatchOutcome();
        }

        var connectionFailures = _placeWarnings.ToList();
        var unfixed = _unfixedIssues.ToList();

        return connectionFailures.Count == 0 && unfixed.Count == 0
            ? RoutingResult.Ok(SerializePlacedGuids())
            : RoutingResult.Fail(BuildFeedback(connectionFailures, unfixed), "Placement reported problems.", GH_RuntimeMessageLevel.Warning);
    }

    /// <summary>
    /// Shapes the routing result for a patch application: a clean apply routes the added and
    /// modified GUIDs forward (the Canvas Observation's scan scope); conflicts route feedback back
    /// that tells the model exactly which operations did NOT apply — everything else did, so it
    /// must resubmit only the corrected operations.
    /// </summary>
    /// <returns>The routing result.</returns>
    private RoutingResult ReadPatchOutcome()
    {
        if (_patchOutcome is not { } outcome)
        {
            return RoutingResult.Fail("The patch produced no outcome.", "The patch produced no outcome.", GH_RuntimeMessageLevel.Error);
        }

        if (outcome.Success)
        {
            return RoutingResult.Ok(SerializeGuids(outcome.AddedGuids.Concat(outcome.ModifiedGuids)));
        }

        // A hard failure (nothing touched) carries its own model-facing message; a partial apply
        // gets the op-by-op breakdown.
        GH_RuntimeMessageLevel level = outcome.ErrorMessage is null
            ? GH_RuntimeMessageLevel.Warning
            : GH_RuntimeMessageLevel.Error;

        return RoutingResult.Fail(
            outcome.ErrorMessage ?? BuildPatchFeedback(outcome),
            "The patch reported problems.",
            level);
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        Rhino.RhinoApp.Idle -= OnIdlePlace;
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendItem(
            menu,
            "Reset Placement Target",
            (_, _) =>
            {
                ResetPlacementTarget();
                Grasshopper.Instances.RedrawCanvas();
            },
            _placementOffset.HasValue);

        ToolStripMenuItem lenient = Menu_AppendItem(
            menu,
            "Apply patches on base mismatch (lenient)",
            (_, _) => _lenientBase = !_lenientBase,
            enabled: true,
            _lenientBase);
        lenient.ToolTipText = "By default a ghpatch is refused when the canvas changed since the model last saw it "
            + "(its base checksum no longer matches). Enable to apply such patches anyway — components matched by "
            + "instanceGuid still resolve correctly, but wires addressed by integer id may land on the wrong component.";
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        if (_placementOffset is { } off)
        {
            writer.SetDrawingPointF("PlacementOffset", off);
        }

        writer.SetBoolean("LenientBase", _lenientBase);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _placementOffset = reader.ItemExists("PlacementOffset")
            ? reader.GetDrawingPointF("PlacementOffset")
            : null;

        _lenientBase = reader.ItemExists("LenientBase") && reader.GetBoolean("LenientBase");
        return base.Read(reader);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _placedGuids.Clear();
        _pushError = null;
        _pendingJson = string.Empty;
        _pendingIsPatch = false;
        _patchOutcome = null;
        _placeWarnings = Array.Empty<string>();
        _unfixedIssues = Array.Empty<string>();
    }

    /// <summary>
    /// One-shot idle handler that removes the previous placement and places the pending graph
    /// after the solution settles, then hands control back to the routing base.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnIdlePlace(object? sender, EventArgs e)
    {
        Rhino.RhinoApp.Idle -= OnIdlePlace;

        try
        {
            RectangleF bounds = Attributes.Bounds;
            PointF targetOrigin = PlacementTarget ?? new PointF(bounds.Right + PlacementGap, bounds.Y);

            if (_pendingIsPatch)
            {
                // Patch mode edits the existing canvas in place — nothing is removed first, and the
                // canvas itself (not this component's bookkeeping) is the source of truth for what
                // exists. A successful patch also retires the previous-placement list: those
                // components have been adopted into the user's definition, so a later full-graph
                // run must place alongside them, not delete them.
                _patchOutcome = GhJsonBridge.ApplyPatchToCanvas(_pendingJson, targetOrigin, verifyBase: !_lenientBase);
                if (_patchOutcome.Success)
                {
                    _placedGuids.Clear();
                }
            }
            else
            {
                RemovePreviouslyPlaced();
                _placeWarnings = Array.Empty<string>();
                _unfixedIssues = Array.Empty<string>();

                PlaceResult result = GhJsonBridge.LoadAndPlaceJson(_pendingJson, targetOrigin);
                _unfixedIssues = result.UnfixedIssues;

                if (!result.Success)
                {
                    _pushError = $"Placement failed: {result.ErrorMessage}";
                }
                else
                {
                    _placedGuids.Clear();
                    _placedGuids.AddRange(result.PlacedGuids);
                    _placeWarnings = result.Warnings;
                }
            }
        }
        catch (Exception ex)
        {
            _pushError = $"Placement failed: {ex.Message}";
        }

        RequestReadPass();
    }

    /// <summary>
    /// Removes the components placed by the previous run, so each run replaces rather than
    /// stacks. Removing an object drops its wires automatically.
    /// </summary>
    private void RemovePreviouslyPlaced()
    {
        GH_Document? doc = OnPingDocument();
        if (doc is not null)
        {
            foreach (Guid g in _placedGuids)
            {
                if (doc.FindObject(g, false) is IGH_DocumentObject obj)
                {
                    doc.RemoveObject(obj, false);
                }
            }
        }

        _placedGuids.Clear();
    }

    /// <summary>
    /// Serialises the placed components' GUIDs as newline-separated values for the Success
    /// payload, so a downstream Canvas Observation can scope its runtime-health scan to exactly this
    /// placement.
    /// </summary>
    /// <returns>The placed GUIDs, one per line.</returns>
    private string SerializePlacedGuids() =>
        SerializeGuids(_placedGuids);

    private static string SerializeGuids(IEnumerable<Guid> guids) =>
        string.Join(Environment.NewLine, guids.Select(g => g.ToString()));

    /// <summary>
    /// Builds the feedback payload routed back on the Fail Signal for a partially applied patch.
    /// The ghpatch policy is "apply what can be applied, report the rest", so the wording must
    /// stop the model from re-emitting the operations that DID land.
    /// </summary>
    /// <param name="outcome">The patch outcome carrying the conflicts.</param>
    /// <returns>A model-facing feedback string.</returns>
    private static string BuildPatchFeedback(CanvasPatchOutcome outcome)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Your ghpatch was PARTIALLY applied. The operations listed below did NOT apply; every other operation DID land on the canvas, so do not re-emit it. Re-read the current canvas state and resubmit a patch containing ONLY the corrected operations.");

        sb.AppendLine();
        sb.AppendLine("Operations that did not apply:");
        foreach (string conflict in outcome.Conflicts)
        {
            sb.AppendLine($"  - {conflict}");
        }

        if (outcome.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Warnings:");
            foreach (string warning in outcome.Warnings)
            {
                sb.AppendLine($"  - {warning}");
            }
        }

        int applied = outcome.AddedGuids.Count + outcome.ModifiedGuids.Count + outcome.RemovedGuids.Count;
        sb.AppendLine();
        sb.AppendLine($"Applied successfully: {outcome.AddedGuids.Count} added, {outcome.ModifiedGuids.Count} modified, {outcome.RemovedGuids.Count} removed ({applied} operations in total).");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Builds the feedback payload routed back on the Fail Signal, listing the mechanical
    /// placement problems the model must fix in its GhJSON.
    /// </summary>
    /// <param name="connectionFailures">Wires the library could not create (id/paramIndex mismatch).</param>
    /// <param name="unfixedIssues">Issues the GhJSON fixer could not repair before placement.</param>
    /// <returns>A human-readable feedback string.</returns>
    private static string BuildFeedback(IReadOnlyList<string> connectionFailures, IReadOnlyList<string> unfixedIssues)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The GhJSON graph you generated could not be placed cleanly. Please fix and resubmit.");

        if (unfixedIssues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Structural issues that could not be auto-repaired (check component names and ids):");
            foreach (string issue in unfixedIssues)
            {
                sb.AppendLine($"  - {issue}");
            }
        }

        if (connectionFailures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Connections that could not be created (check each endpoint's id and paramIndex):");
            foreach (string failure in connectionFailures)
            {
                sb.AppendLine($"  - {failure}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
