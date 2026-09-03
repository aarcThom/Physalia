// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.Grounding;
using Physalia.GH.Goo;
using Physalia.GH.Parameters;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Physalia.GH.Components;

/// <summary>
/// Emits a <see cref="RhinoDocumentGrounding"/> describing what is already in the active Rhino
/// document — object count and kinds, the layer table, the overall extents, and what the user has
/// selected. The Rhino-side counterpart of Canvas State, which describes the Grasshopper canvas.
/// Wire its output into the Conversation Log's Grounding input. It has no inputs: it reads the
/// document on every solve.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> A model that can run scripts will otherwise spend its first turn
/// running one just to find out where it is — a signal trace of a scripting session opened with
/// exactly that probe, and the round it cost bought only the layer table and an object count. Put
/// those in the prompt and the first script it writes can be the one that does the work.</para>
/// <para><b>Refresh is where this differs from every other grounder, and the difference is
/// load-bearing.</b> Grasshopper expires components along its own data graph, and a change to the
/// RHINO document is not on that graph — editing geometry in Rhino runs no Grasshopper solution at
/// all, on the host document or in a harness. So this watches Rhino's own document events instead.
/// What those handlers must do is <see cref="GH_DocumentObject.ExpireSolution"/> with
/// <c>recompute: false</c> and nothing more: marking dirty is enough, because this component sits
/// upstream of the Conversation Log and the solve the user's next prompt causes will recompute it
/// before the prompt is assembled. Posting a <c>ScheduleSolution</c> here would be the Script I/O
/// trap — a sub-document is only re-enabled when its proxy solves, and a disabled one silently
/// drops scheduled callbacks, so the stale summary would reach the model anyway.</para>
/// <para><b>No throttle, deliberately.</b> Canvas State rate-limits its watcher because that
/// watcher has to serialize the canvas before it can tell whether anything changed. This one does
/// no work at all in the handler, so a script adding five hundred objects costs five hundred flag
/// sets and one rescan on the next solve. Adding a throttle here would buy nothing and could drop
/// the last event of a burst.</para>
/// </remarks>
public class RhinoDocumentGrounder : PhyBase
{
    private const int OutGrounding = 0;

    // Caps. A document holding ten thousand objects must contribute a section of roughly the same
    // size as one holding ten, or the grounding grows without bound and crowds out the conversation.
    private const int MaxLayers = 25;
    private const int MaxTypes = 8;

    private bool _watching;

    /// <summary>
    /// Initializes a new instance of the <see cref="RhinoDocumentGrounder"/> class.
    /// </summary>
    public RhinoDocumentGrounder()
        : base(
            "Rhino Document",
            "RhDoc",
            "Tells the model what is already in the Rhino document — how much, of what kind, on which layers, how big, and what is selected — so it does not have to spend a turn looking.",
            "Grounding")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("8A5E2C74-91F3-4B06-BD28-6E0C7A93F51D");

    /// <inheritdoc/>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        StartWatching();
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        StopWatching();
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // No inputs: everything is read from the active Rhino document.
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(
            new Param_Grounding(),
            "Grounding",
            "Gnd",
            "What is in the Rhino document right now, summarised for the model. Wire into a Conversation Log's Grounding input.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Re-arm on every solve as well as on AddedToDocument: a component restored from a saved
        // file, pasted, or brought in with a preset reaches its first solve by paths that do not
        // all run AddedToDocument first.
        StartWatching();

        DA.SetData(OutGrounding, new GH_Grounding(ReadDocument()));
    }

    private static RhinoDocumentGrounding ReadDocument()
    {
        RhinoDoc? doc = RhinoDoc.ActiveDoc;
        if (doc is null)
        {
            return new RhinoDocumentGrounding(0, Array.Empty<string>(), Array.Empty<RhinoLayerSummary>(), 0, null, 0);
        }

        var typeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var layerCounts = new Dictionary<int, int>();
        int total = 0;

        // One pass: the type histogram and the per-layer tally come from the same walk, because a
        // large document is the case that matters and walking it twice would double the cost of
        // every round.
        foreach (RhinoObject obj in doc.Objects)
        {
            total++;

            string type = obj.ObjectType.ToString();
            typeCounts[type] = typeCounts.TryGetValue(type, out int n) ? n + 1 : 1;

            int layer = obj.Attributes.LayerIndex;
            layerCounts[layer] = layerCounts.TryGetValue(layer, out int m) ? m + 1 : 1;
        }

        List<string> types = typeCounts
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Take(MaxTypes)
            .Select(p => $"{p.Value} {p.Key}")
            .ToList();

        var layers = new List<RhinoLayerSummary>();
        foreach (Layer layer in doc.Layers)
        {
            if (layer.IsDeleted)
            {
                continue;
            }

            layerCounts.TryGetValue(layer.Index, out int count);
            layers.Add(new RhinoLayerSummary(layer.FullPath, count, !layer.IsVisible, layer.IsLocked));
        }

        // Populated layers first: an empty layer is worth mentioning (someone made it on purpose)
        // but never at the expense of one holding the geometry under discussion.
        List<RhinoLayerSummary> ordered = layers
            .OrderByDescending(l => l.ObjectCount)
            .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int omitted = Math.Max(0, ordered.Count - MaxLayers);
        if (omitted > 0)
        {
            ordered = ordered.Take(MaxLayers).ToList();
        }

        return new RhinoDocumentGrounding(
            total,
            types,
            ordered,
            omitted,
            DescribeExtents(doc, total),
            CountSelected(doc));
    }

    // "X 0 to 10000, Y 0 to 8000, Z -300 to 6200" — axis-by-axis rather than two corner points,
    // because the model reads this to judge scale and placement, and a pair of Point3d triples
    // makes it do the subtraction itself.
    private static string? DescribeExtents(RhinoDoc doc, int objectCount)
    {
        if (objectCount <= 0)
        {
            return null;
        }

        BoundingBox box = doc.Objects.BoundingBox;
        if (!box.IsValid)
        {
            return null;
        }

        return $"X {Round(box.Min.X)} to {Round(box.Max.X)}, "
            + $"Y {Round(box.Min.Y)} to {Round(box.Max.Y)}, "
            + $"Z {Round(box.Min.Z)} to {Round(box.Max.Z)}";
    }

    private static string Round(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static int CountSelected(RhinoDoc doc)
    {
        try
        {
            return doc.Objects.GetSelectedObjects(includeLights: false, includeGrips: false).Count();
        }
        catch (Exception)
        {
            // Selection is a nicety; never let reading it cost the rest of the grounding.
            return 0;
        }
    }

    private void StartWatching()
    {
        if (_watching)
        {
            return;
        }

        _watching = true;

        RhinoDoc.AddRhinoObject += OnDocumentChanged;
        RhinoDoc.DeleteRhinoObject += OnDocumentChanged;
        RhinoDoc.UndeleteRhinoObject += OnDocumentChanged;
        RhinoDoc.ReplaceRhinoObject += OnDocumentChanged;
        RhinoDoc.ModifyObjectAttributes += OnDocumentChanged;
        RhinoDoc.AfterTransformObjects += OnDocumentChanged;
        RhinoDoc.LayerTableEvent += OnDocumentChanged;
        RhinoDoc.SelectObjects += OnDocumentChanged;
        RhinoDoc.DeselectObjects += OnDocumentChanged;
        RhinoDoc.DeselectAllObjects += OnDocumentChanged;
        RhinoDoc.NewDocument += OnDocumentChanged;
        RhinoDoc.EndOpenDocument += OnDocumentChanged;
        RhinoDoc.ActiveDocumentChanged += OnDocumentChanged;
    }

    private void StopWatching()
    {
        if (!_watching)
        {
            return;
        }

        _watching = false;

        RhinoDoc.AddRhinoObject -= OnDocumentChanged;
        RhinoDoc.DeleteRhinoObject -= OnDocumentChanged;
        RhinoDoc.UndeleteRhinoObject -= OnDocumentChanged;
        RhinoDoc.ReplaceRhinoObject -= OnDocumentChanged;
        RhinoDoc.ModifyObjectAttributes -= OnDocumentChanged;
        RhinoDoc.AfterTransformObjects -= OnDocumentChanged;
        RhinoDoc.LayerTableEvent -= OnDocumentChanged;
        RhinoDoc.SelectObjects -= OnDocumentChanged;
        RhinoDoc.DeselectObjects -= OnDocumentChanged;
        RhinoDoc.DeselectAllObjects -= OnDocumentChanged;
        RhinoDoc.NewDocument -= OnDocumentChanged;
        RhinoDoc.EndOpenDocument -= OnDocumentChanged;
        RhinoDoc.ActiveDocumentChanged -= OnDocumentChanged;
    }

    // Every Rhino document event lands here and does exactly one thing: mark this component dirty.
    // Generic so one handler serves thirteen differently-typed events; the argument is never read,
    // because "something changed, rescan next time" is the whole decision.
    private void OnDocumentChanged<T>(object? sender, T e)
        where T : EventArgs
    {
        // recompute:false — see the class remarks. This must NOT ask for a solution: a Rhino edit
        // reaches no Grasshopper solve, and a scheduled one is dropped outright inside a harness
        // whose proxy has not solved.
        ExpireSolution(false);
    }
}
