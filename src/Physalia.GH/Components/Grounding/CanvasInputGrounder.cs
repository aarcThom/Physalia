// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;

namespace Physalia.GH.Components;

/// <summary>
/// Grounds the model with the inputs already on the canvas — the Rhino-referenced parameters dropped by
/// the <see cref="RhinoGeometryTool"/> — so a graph the model generates can wire onto an existing input
/// (by its name) instead of duplicating it. Emits a single <see cref="CanvasInputGrounding"/>; wire the
/// output into a Recorder's Grounding input alongside any other grounding.
///
/// <para>The list refreshes live as inputs are created or removed: the geometry tool places its
/// parameters on <c>RhinoApp.Idle</c> (outside a solve), which triggers a new solution, so this
/// component watches the document's solution end and re-solves itself only when the referenceable set
/// actually changes — the new grounding reaches the Recorder without a runaway solve loop.</para>
/// </summary>
public class CanvasInputGrounder : PhyBase
{
    private const int OutGrounding = 0;

    private string _lastSignature = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="CanvasInputGrounder"/> class.
    /// </summary>
    public CanvasInputGrounder()
        : base("Canvas Inputs", "CvsIn", "Grounds the model with the Rhino-referenced inputs already on the canvas so it can reference them by name instead of recreating them. Wire into a Recorder's Grounding input.", "Grounding")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B1E6F2A0-3C47-4D8A-9B21-5E0D7A9C4F13");

    /// <inheritdoc/>
    /// <remarks>
    /// Watches the document so the list refreshes the moment an input is placed or deleted — such a
    /// change does not re-solve this (the source) component, so the refresh is driven off the end of the
    /// solution that carried the change.
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
        // No inputs — the component discovers canvas inputs by scanning the document.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new Param_Grounding(), "Grounding", "Gnd", "Grounding listing the Rhino-referenced inputs already on the canvas. Wire into the Recorder's Grounding input.", GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        var inputs = CanvasInputs(OnPingDocument());
        _lastSignature = Signature(inputs);
        DA.SetData(OutGrounding, new GH_Grounding(new CanvasInputGrounding(inputs)));
    }

    // Projects the document's referenceable inputs to the Core grounding shape (name + type).
    private static List<CanvasInput> CanvasInputs(GH_Document? doc) =>
        RhinoGeometryTool.CollectReferenceableInputs(doc)
            .Select(i => new CanvasInput(i.Name, i.TypeName))
            .ToList();

    // A stable, order-independent signature of the referenceable set so a change (input added, removed,
    // or renamed) is detected without re-solving when nothing changed.
    private static string Signature(IEnumerable<CanvasInput> inputs) =>
        string.Join("|", inputs.Select(i => $"{i.Name}:{i.TypeName}").OrderBy(s => s, StringComparer.Ordinal));

    private void OnDocumentSolutionEnd(object sender, GH_SolutionEventArgs e)
    {
        // An input may have been placed (on Idle) or deleted during this solution, which does not
        // re-solve this source component. Re-solve only when the referenceable set actually changed, so
        // the refreshed grounding reaches the Recorder and the comparison breaks any solve loop once it
        // converges.
        if (Signature(CanvasInputs(OnPingDocument())) != _lastSignature)
        {
            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(false));
        }
    }
}
