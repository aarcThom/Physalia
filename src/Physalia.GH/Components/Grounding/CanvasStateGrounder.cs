// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Generation;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Grounds the model with the CURRENT state of the canvas — the user's work product serialized as
/// GhJSON — so it can edit the definition incrementally by emitting a ghpatch instead of
/// regenerating whole graphs. Emits a single <see cref="CanvasStateGrounding"/>; wire the output
/// into a Conversation Log's Grounding input alongside any other grounding. The export excludes
/// Physalia's own pipeline components and is produced by the same code path the Component
/// Transmitter uses as the patch's base reference frame, so what the model sees and what a patch
/// applies against can never disagree.
///
/// <para>The snapshot refreshes live: the component watches the document's solution end and
/// re-solves itself only when the exported state's checksum actually changes. Re-exports are
/// rate-limited so a slider drag (one solution per tick) does not serialize the canvas on every
/// tick, with a trailing re-check so the final state is never missed; exports are also skipped
/// while a Physalia placement is mutating the canvas mid-import.</para>
/// </summary>
public class CanvasStateGrounder : PhyBase
{
    private const int OutGrounding = 0;

    // Minimum interval between SolutionEnd-driven re-exports. A slider drag ends a solution per
    // tick; serializing a large canvas at that rate would make dragging feel sticky.
    private static readonly TimeSpan RescanInterval = TimeSpan.FromMilliseconds(300);

    private string _lastChecksum = string.Empty;
    private DateTime _lastScanUtc = DateTime.MinValue;
    private bool _trailingCheckScheduled;

    /// <summary>
    /// Initializes a new instance of the <see cref="CanvasStateGrounder"/> class.
    /// </summary>
    public CanvasStateGrounder()
        : base("Canvas State", "CvsSt", "Grounds the model with the current canvas state as GhJSON so it can edit the definition incrementally (via ghpatch) instead of regenerating it. Wire into a Conversation Log's Grounding input.", "Grounding")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("A4C8E7D2-91B5-4F63-8D0A-6E2F3B7C5A19");

    /// <inheritdoc/>
    /// <remarks>
    /// Watches the document so the snapshot refreshes when the canvas changes — a user edit or a
    /// transmitter placement does not re-solve this (source) component, so the refresh is driven
    /// off the end of the solution that carried the change.
    /// </remarks>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        document.SolutionEnd += OnDocumentSolutionEnd;
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        document.SolutionEnd -= OnDocumentSolutionEnd;
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs — the component snapshots the canvas by scanning the document.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Grounding(), "Grounding", "Gnd", "Grounding carrying the current canvas state as GhJSON. Wire into the Conversation Log's Grounding input.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        GhJsonBridge.CanvasStateSnapshot? snapshot = GhJsonBridge.TryExportCanvasState(OnPingDocument());
        _lastChecksum = snapshot?.Checksum ?? string.Empty;
        _lastScanUtc = DateTime.UtcNow;

        var grounding = new CanvasStateGrounding(
            snapshot?.Json ?? string.Empty,
            snapshot?.Checksum ?? string.Empty,
            snapshot?.ComponentCount ?? 0);

        DA.SetData(OutGrounding, new GH_Grounding(grounding));
    }

    private void OnDocumentSolutionEnd(object sender, GH_SolutionEventArgs e)
    {
        // A placement in progress churns the canvas several times before settling; the transmitter
        // triggers a solution once it is done, and that one refreshes the snapshot.
        if (GhJsonBridge.IsImporting)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (now - _lastScanUtc < RescanInterval)
        {
            // Mid-storm (e.g. a slider drag): skip this tick but leave one trailing re-check
            // behind so the state reached when the storm ends is always picked up.
            if (!_trailingCheckScheduled)
            {
                _trailingCheckScheduled = true;
                OnPingDocument()?.ScheduleSolution((int)RescanInterval.TotalMilliseconds + 50, _ =>
                {
                    _trailingCheckScheduled = false;
                    RescanAndExpireOnChange();
                });
            }

            return;
        }

        RescanAndExpireOnChange();
    }

    // Re-exports the canvas and re-solves this component only when the checksum actually changed,
    // so the refreshed grounding reaches the Conversation Log and the comparison breaks the solve
    // loop once it converges (this component's own solve ends a solution too).
    private void RescanAndExpireOnChange()
    {
        _lastScanUtc = DateTime.UtcNow;

        GhJsonBridge.CanvasStateSnapshot? snapshot = GhJsonBridge.TryExportCanvasState(OnPingDocument());
        if ((snapshot?.Checksum ?? string.Empty) != _lastChecksum)
        {
            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(false));
        }
    }
}
