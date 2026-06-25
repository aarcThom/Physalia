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
using Physalia.GH.Attributes;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Generation;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// Takes an LLM-generated GhJSON graph (arriving as the consumed signal's payload), places
/// the whole graph on the canvas to the right of this component, then routes the outcome.
/// This component reports only <b>mechanical</b> placement problems — invalid GhJSON, a failed
/// placement, wires the library could not create, and structural issues the fixer could not
/// repair — back on the Fail Signal so the model can fix and resubmit. A clean placement routes
/// the placed components' GUIDs forward on the Success Signal; an Observer wired downstream
/// scopes its runtime-health scan (errors, warnings, dead components) to exactly those GUIDs.
/// Each run removes the previous placement before placing the new graph.
/// </summary>
public class ComponentTransmitter : RoutingComponentBase<string>, IHarnessArrow
{
    private const float PlacementGap = 50f;

    private readonly List<Guid> _placedGuids = new();
    private string _pendingJson = string.Empty;
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
            "Places an LLM-generated GhJSON graph on the canvas. Clean placement routes the placed components' GUIDs forward (for an Observer to scan); mechanical placement problems route a description back.",
            "Serializers")
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

    // IHarnessArrow — lets a collapsed Chatbox proxy delegate its bottom arrow to this transmitter.
    // Mirrors CompTxAttrib: orange→orchid wire to a free canvas point that a drop simply stores.

    /// <inheritdoc/>
    WireGradient IHarnessArrow.ArrowGradient => new WireGradient(Color.Orange, Color.MediumOrchid);

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

        if (!GhJsonBridge.IsValidJson(data, out string? message))
        {
            _pushError = $"Not valid GhJSON: {message}";
            RequestReadPass();
            return;
        }

        _pendingJson = data;

        // Put() adds objects and triggers its own NewSolution, so it must run outside the
        // current solution. RhinoApp.Idle fires on the UI thread once the solution has settled.
        Rhino.RhinoApp.Idle += OnIdlePlace;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reports only mechanical placement outcomes (known as soon as placement finishes), so it
    /// need not wait for the placed components to solve — an Observer wired downstream owns the
    /// runtime-health gate and scan.
    /// </remarks>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        if (_pushError != null)
        {
            return RoutingResult.Fail(_pushError, _pushError, GH_RuntimeMessageLevel.Error);
        }

        var connectionFailures = _placeWarnings.ToList();
        var unfixed = _unfixedIssues.ToList();

        return connectionFailures.Count == 0 && unfixed.Count == 0
            ? RoutingResult.Ok(SerializePlacedGuids())
            : RoutingResult.Fail(BuildFeedback(connectionFailures, unfixed), "Placement reported problems.", GH_RuntimeMessageLevel.Warning);
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
    }

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        if (_placementOffset is { } off)
        {
            writer.SetDrawingPointF("PlacementOffset", off);
        }

        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _placementOffset = reader.ItemExists("PlacementOffset")
            ? reader.GetDrawingPointF("PlacementOffset")
            : null;

        return base.Read(reader);
    }

    /// <inheritdoc/>
    protected override void OnCleared()
    {
        base.OnCleared();
        _placedGuids.Clear();
        _pushError = null;
        _pendingJson = string.Empty;
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
            RemovePreviouslyPlaced();
            _placeWarnings = Array.Empty<string>();
            _unfixedIssues = Array.Empty<string>();

            RectangleF bounds = Attributes.Bounds;
            PointF targetOrigin = PlacementTarget ?? new PointF(bounds.Right + PlacementGap, bounds.Y);
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
    /// payload, so a downstream Observer can scope its runtime-health scan to exactly this
    /// placement.
    /// </summary>
    /// <returns>The placed GUIDs, one per line.</returns>
    private string SerializePlacedGuids() =>
        string.Join(Environment.NewLine, _placedGuids.Select(g => g.ToString()));

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
