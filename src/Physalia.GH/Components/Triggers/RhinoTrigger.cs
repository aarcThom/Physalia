// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Physalia.Core.Triggers;
using Rhino;

namespace Physalia.GH.Components;

/// <summary>
/// Fires when the Rhino document changes, while armed. The trigger that turns Physalia from a
/// generator into a participant: the user moves something, and the pipeline responds.
///
/// <para><b>Grasshopper cannot see this happen.</b> GH expires components along its own data graph,
/// and a change to the Rhino document is not on that graph — editing geometry runs no Grasshopper
/// solution anywhere, host or harness. So this listens to Rhino's own document events, exactly as the
/// Rhino Document grounder does. What it does with them is the difference: the grounder marks itself
/// dirty and waits to be recomputed by the next prompt, because it only has to be current WHEN asked;
/// this one has to make something happen, so it wakes the pipeline itself.</para>
///
/// <para><b>Watch Selection is the one to reach for first.</b> "Move these", "make this taller", "what
/// is wrong with that connection" — a phrase like that only resolves if a round starts when the
/// selection changes, and the Rhino Document grounder is already carrying the selection into the
/// prompt. Geometry is on by default because it is what most pipelines mean by "the model changed";
/// selection is off by default because clicking about in a viewport is not, on its own, a request.</para>
///
/// <para><b>The payload says what changed, not what is there.</b> What is there is the grounder's job
/// and it says it in one voice already. What a wake-up has to add is the fact of the change, because
/// by the time the model reads the prompt a change is indistinguishable from the state it was always
/// in.</para>
/// </summary>
public class RhinoTrigger : SignalSourceBase<RhinoChangeKind>
{
    private const int InGeometry = 0;
    private const int InSelection = 1;
    private const int InLayers = 2;
    private const int InNewFile = 3;

    private bool _watching;

    private RhinoChangeKind _watched = RhinoChangeKind.Geometry;

    /// <summary>
    /// Initializes a new instance of the <see cref="RhinoTrigger"/> class.
    /// </summary>
    public RhinoTrigger()
        : base(
            "Rhino Changed",
            "RhWatch",
            "Starts a round when the Rhino document changes — geometry edited, the selection changed, layers added. Right-click and tick Armed to switch it on; it is always off when a file opens.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D8B6402F-95C1-4E73-A2D9-71F0C3B85A4E");

    /// <inheritdoc/>
    protected override string SignalOutputDescription =>
        "Fires once per burst of Rhino edits, saying what kind of change it was and how much is selected. Wire into a Conversation Log's Prompt Signal input, next to a Rhino Document grounder that describes the state.";

    /// <inheritdoc/>
    protected override string ArmedCaption
    {
        get
        {
            var parts = new List<string>(4);
            if (_watched.HasFlag(RhinoChangeKind.Geometry))
            {
                parts.Add("geo");
            }

            if (_watched.HasFlag(RhinoChangeKind.Selection))
            {
                parts.Add("sel");
            }

            if (_watched.HasFlag(RhinoChangeKind.Layers))
            {
                parts.Add("layers");
            }

            if (_watched.HasFlag(RhinoChangeKind.Document))
            {
                parts.Add("file");
            }

            return parts.Count == 0 ? "nothing watched" : string.Join("+", parts);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Half a second, and it is load-bearing here more than anywhere else: a script adding five
    /// hundred objects raises five hundred events, and a dragged gumball raises one per frame.
    /// </remarks>
    protected override int SettleMs => 500;

    /// <inheritdoc/>
    protected override void RegisterSourceInputs(GH_InputParamManager pManager)
    {
        pManager.AddBooleanParameter(
            "Geometry",
            "G",
            "Fire when objects are added, deleted, replaced, moved, or their layer or colour changed.",
            GH_ParamAccess.item,
            true);
        pManager.AddBooleanParameter(
            "Selection",
            "S",
            "Fire when the user's selection changes. This is what makes \"move these\" mean something — the Rhino Document grounder carries the selection into the prompt.",
            GH_ParamAccess.item,
            false);
        pManager.AddBooleanParameter(
            "Layers",
            "L",
            "Fire when the layer table changes.",
            GH_ParamAccess.item,
            false);
        pManager.AddBooleanParameter(
            "New File",
            "N",
            "Fire when a different Rhino file is opened or created. Off by default: opening a file is rarely a request.",
            GH_ParamAccess.item,
            false);
    }

    /// <inheritdoc/>
    protected override void OnSolveTick(IGH_DataAccess da)
    {
        bool geometry = true;
        bool selection = false;
        bool layers = false;
        bool newFile = false;
        da.GetData(InGeometry, ref geometry);
        da.GetData(InSelection, ref selection);
        da.GetData(InLayers, ref layers);
        da.GetData(InNewFile, ref newFile);

        RhinoChangeKind watched = RhinoChangeKind.None;
        if (geometry)
        {
            watched |= RhinoChangeKind.Geometry;
        }

        if (selection)
        {
            watched |= RhinoChangeKind.Selection;
        }

        if (layers)
        {
            watched |= RhinoChangeKind.Layers;
        }

        if (newFile)
        {
            watched |= RhinoChangeKind.Document;
        }

        if (watched == RhinoChangeKind.None)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                "Nothing is being watched, so this can never fire. Switch on at least one kind of change.");
        }

        if (watched != _watched)
        {
            _watched = watched;
            UpdateStateDisplay();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every event is subscribed once and filtered at report time rather than subscribed per switch.
    /// The handlers do nothing but classify, so the cost of hearing an unwanted event is a flag test,
    /// and re-subscribing thirteen events every time a boolean input changes is the more fragile half
    /// of the trade.
    /// </remarks>
    protected override void StartListening()
    {
        if (_watching)
        {
            return;
        }

        _watching = true;

        RhinoDoc.AddRhinoObject += OnGeometry;
        RhinoDoc.DeleteRhinoObject += OnGeometry;
        RhinoDoc.UndeleteRhinoObject += OnGeometry;
        RhinoDoc.ReplaceRhinoObject += OnGeometry;
        RhinoDoc.ModifyObjectAttributes += OnGeometry;
        RhinoDoc.AfterTransformObjects += OnGeometry;
        RhinoDoc.LayerTableEvent += OnLayers;
        RhinoDoc.SelectObjects += OnSelection;
        RhinoDoc.DeselectObjects += OnSelection;
        RhinoDoc.DeselectAllObjects += OnSelection;
        RhinoDoc.NewDocument += OnDocument;
        RhinoDoc.EndOpenDocument += OnDocument;
        RhinoDoc.ActiveDocumentChanged += OnDocument;
    }

    /// <inheritdoc/>
    protected override void StopListening()
    {
        if (!_watching)
        {
            return;
        }

        _watching = false;

        RhinoDoc.AddRhinoObject -= OnGeometry;
        RhinoDoc.DeleteRhinoObject -= OnGeometry;
        RhinoDoc.UndeleteRhinoObject -= OnGeometry;
        RhinoDoc.ReplaceRhinoObject -= OnGeometry;
        RhinoDoc.ModifyObjectAttributes -= OnGeometry;
        RhinoDoc.AfterTransformObjects -= OnGeometry;
        RhinoDoc.LayerTableEvent -= OnLayers;
        RhinoDoc.SelectObjects -= OnSelection;
        RhinoDoc.DeselectObjects -= OnSelection;
        RhinoDoc.DeselectAllObjects -= OnSelection;
        RhinoDoc.NewDocument -= OnDocument;
        RhinoDoc.EndOpenDocument -= OnDocument;
        RhinoDoc.ActiveDocumentChanged -= OnDocument;
    }

    /// <inheritdoc/>
    protected override string ComposePayload(IReadOnlyList<RhinoChangeKind> events)
    {
        RhinoChangeKind kinds = events.Aggregate(RhinoChangeKind.None, (all, one) => all | one);

        // Counted at fire time, on the UI thread, rather than carried on each event: the burst is over
        // by now, so this is the state the model will actually be looking at.
        RhinoDoc? doc = RhinoDoc.ActiveDoc;
        int total = 0;
        int selected = 0;

        if (doc is not null)
        {
            total = doc.Objects.Count;

            try
            {
                selected = doc.Objects.GetSelectedObjects(includeLights: false, includeGrips: false).Count();
            }
            catch (Exception)
            {
                // Selection is a nicety; never let reading it cost the wake-up.
                selected = 0;
            }
        }

        return RhinoChangeSummary.Describe(kinds, total, selected);
    }

    private void OnGeometry<T>(object? sender, T e)
        where T : EventArgs => Classify(RhinoChangeKind.Geometry);

    private void OnSelection<T>(object? sender, T e)
        where T : EventArgs => Classify(RhinoChangeKind.Selection);

    private void OnLayers<T>(object? sender, T e)
        where T : EventArgs => Classify(RhinoChangeKind.Layers);

    private void OnDocument<T>(object? sender, T e)
        where T : EventArgs => Classify(RhinoChangeKind.Document);

    private void Classify(RhinoChangeKind kind)
    {
        if (!_watched.HasFlag(kind))
        {
            return;
        }

        ReportEvent(kind);
    }
}
