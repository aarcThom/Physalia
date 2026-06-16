// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Signals;
using Physalia.GH.Generation;

namespace Physalia.GH.Components;

/// <summary>
/// Takes an LLM-generated GhJSON graph (arriving as the consumed signal's payload), places
/// the whole graph on the canvas to the right of this component, waits for the placed
/// components to solve, then routes the outcome. A clean placement routes the GhJSON forward
/// on the Success Signal; runtime errors or improperly-wired (orphaned) components route a
/// description back on the Fail Signal so the model can fix and resubmit. Each run removes the
/// previous placement before placing the new graph.
/// </summary>
public class ComponentTransmitter : RoutingComponentBase<string>
{
    private const float PlacementGap = 50f;

    // How close (in canvas units) a component's pivot X must be to the leftmost placed pivot
    // to count as a legitimate "source" column and be exempt from the orphan wiring check.
    private const float LeftColumnTolerance = 30f;

    private readonly List<Guid> _placedGuids = new();
    private string _pendingJson = string.Empty;
    private string? _pushError;
    private IReadOnlyList<string> _placeWarnings = Array.Empty<string>();
    private IReadOnlyList<string> _unfixedIssues = Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentTransmitter"/> class.
    /// </summary>
    public ComponentTransmitter()
        : base(
            "Component Transmitter",
            "CompTx",
            "Places an LLM-generated GhJSON graph on the canvas and routes the placed components' errors. Clean placement routes the GhJSON forward; problems route a description back.",
            "Serializers")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("4BA76257-AD4C-462C-AB7E-B130DB176BF4");

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
    /// Defers the read pass until every placed component has finished solving, so the runtime
    /// messages read in <see cref="ReadSolve"/> reflect the placed graph rather than a still
    /// expired (Blank) state.
    /// </remarks>
    protected override bool IsReadReady(string data)
    {
        if (_pushError != null)
        {
            return true;
        }

        GH_Document? doc = OnPingDocument();
        if (doc is null)
        {
            return true;
        }

        foreach (Guid g in _placedGuids)
        {
            if (doc.FindObject(g, false) is IGH_ActiveObject ao &&
                (ao.Phase == GH_SolutionPhase.Blank || ao.Phase == GH_SolutionPhase.Computing))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads the placed components' runtime errors and checks for downstream components left
    /// fully unwired, routing Success (the GhJSON) or Fail (a description of the problems)
    /// accordingly.
    /// </remarks>
    protected override RoutingResult ReadSolve(string data, IGH_DataAccess da)
    {
        if (_pushError != null)
        {
            return RoutingResult.Fail(_pushError, _pushError, GH_RuntimeMessageLevel.Error);
        }

        List<string> errors = CollectRuntimeErrors();
        List<string> orphans = CollectOrphanComponents();
        var connectionFailures = _placeWarnings.ToList();
        var unfixed = _unfixedIssues.ToList();

        return errors.Count == 0 && orphans.Count == 0 && connectionFailures.Count == 0 && unfixed.Count == 0
            ? RoutingResult.Ok(_pendingJson)
            : RoutingResult.Fail(BuildFeedback(errors, orphans, connectionFailures, unfixed), "Placed graph reported problems.", GH_RuntimeMessageLevel.Warning);
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        Rhino.RhinoApp.Idle -= OnIdlePlace;
        base.RemovedFromDocument(document);
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
            var targetOrigin = new PointF(bounds.Right + PlacementGap, bounds.Y);
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
    /// Collects the error-level runtime messages reported by the placed components.
    /// </summary>
    /// <returns>One entry per error, prefixed with the reporting object's name.</returns>
    private List<string> CollectRuntimeErrors()
    {
        var errors = new List<string>();
        GH_Document? doc = OnPingDocument();
        if (doc is null)
        {
            return errors;
        }

        foreach (Guid g in _placedGuids)
        {
            if (doc.FindObject(g, false) is IGH_ActiveObject ao)
            {
                foreach (string message in ao.RuntimeMessages(GH_RuntimeMessageLevel.Error))
                {
                    errors.Add($"{ao.Name}: {message}");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Finds placed non-parameter components that sit downstream of the leftmost (source)
    /// column yet have no wired inputs — i.e. components the graph left disconnected. Floating
    /// parameter objects (sliders, panels, params) are exempt, as are components in the
    /// leftmost column, which are legitimate sources.
    /// </summary>
    /// <returns>One entry per orphaned component, naming it and its pivot location.</returns>
    private List<string> CollectOrphanComponents()
    {
        var orphans = new List<string>();
        GH_Document? doc = OnPingDocument();
        if (doc is null)
        {
            return orphans;
        }

        var placed = _placedGuids
            .Select(g => doc.FindObject(g, false))
            .Where(o => o is not null && o.Attributes is not null)
            .ToList();

        if (placed.Count == 0)
        {
            return orphans;
        }

        float minX = placed.Min(o => o!.Attributes.Pivot.X);

        foreach (IGH_DocumentObject? obj in placed)
        {
            if (obj is not IGH_Component comp)
            {
                continue;
            }

            if (comp.Attributes.Pivot.X <= minX + LeftColumnTolerance)
            {
                continue;
            }

            if (comp.Params.Input.Count > 0 && comp.Params.Input.All(p => p.SourceCount == 0))
            {
                PointF pivot = comp.Attributes.Pivot;
                orphans.Add($"{comp.Name} (at {pivot.X:0}, {pivot.Y:0})");
            }
        }

        return orphans;
    }

    /// <summary>
    /// Builds the feedback payload routed back on the Fail Signal, listing runtime errors and
    /// disconnected components.
    /// </summary>
    /// <param name="errors">The collected runtime error messages.</param>
    /// <param name="orphans">The collected disconnected-component descriptions.</param>
    /// <param name="connectionFailures">Wires the library could not create (id/paramIndex mismatch).</param>
    /// <param name="unfixedIssues">Issues the GhJSON fixer could not repair before placement.</param>
    /// <returns>A human-readable feedback string.</returns>
    private static string BuildFeedback(IReadOnlyList<string> errors, IReadOnlyList<string> orphans, IReadOnlyList<string> connectionFailures, IReadOnlyList<string> unfixedIssues)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The GhJSON graph you generated has problems. Please fix and resubmit.");

        if (unfixedIssues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Structural issues that could not be auto-repaired (check component names and ids):");
            foreach (string issue in unfixedIssues)
            {
                sb.AppendLine($"  - {issue}");
            }
        }

        if (errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Errors:");
            foreach (string error in errors)
            {
                sb.AppendLine($"  - {error}");
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

        if (orphans.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Disconnected components (have no wired inputs):");
            foreach (string orphan in orphans)
            {
                sb.AppendLine($"  - {orphan}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
