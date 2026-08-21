// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Goo;
using Physalia.GH.Harness;
using Physalia.GH.Parameters;
using Rhino.Geometry;
using GHPanel = Grasshopper.Kernel.Special.GH_Panel;

namespace Physalia.GH.Components;

/// <summary>
/// Carries data OUT of the harness, onto the user's canvas. The outward half of the harness boundary,
/// and the mirror of <see cref="HarnessIn"/>: that one has no inputs, this one has no outputs, and
/// between them the whole exchange with the canvas happens at the proxy rather than in here.
///
/// <para><b>One input, no outputs.</b> This is an endpoint, not a passthrough — data arrives and
/// leaves the harness. Whatever is wired into <b>Data</b> is written into the target this node's grip
/// on the proxy points at, and the pipeline inside the harness ends here.</para>
///
/// <para><b>Any Grasshopper data.</b> Geometry, numbers, text, booleans, colours, a signal. This
/// replaces the separate Text and Geometry transmitters, which differed only in the parameter type
/// they happened to declare — and between them still refused booleans, integers and colours. A
/// generic parameter takes all of it, and the target does the converting:
/// <see cref="ParamTargets.WriteTree{T}"/> casts each item with the target parameter's own
/// <c>Cast_Object</c>, which is the very conversion a wire performs, so a Brep entering a Mesh input
/// converts exactly as it would have through one.</para>
///
/// <para><b>The tree is data.</b> It survives the delivery branch for branch. Stringifying is per ITEM
/// and keeps the container, so a text parameter takes the whole tree; a Panel, which can hold only one
/// string, gets one item per line — a LIST of the same length — because that is the only shape a
/// Panel's single string reads back as, and a multi-branch tree sent to one is flattened with a
/// warning saying so.</para>
///
/// <para><b>One name, two ends.</b> The input's nickname is what the harness proxy paints beside this
/// node's grip, starting out "Data". Rename the input and the label on the harness follows, so the
/// grip out there always says what is going through it. The proxy's own end is a label we paint rather
/// than a parameter, so there is nothing to rename out there — unlike a Harness In, whose pair of
/// names can be edited from either side.</para>
///
/// <para>The grip connects like an ordinary Grasshopper output: drag it onto the <b>input grip</b>
/// downstream that should receive the values and it stays connected, the wire drawn to that grip and
/// the values delivered on every change. Any input will do, and one that cannot read what arrives says
/// so rather than going quietly empty. Ctrl+drop on the target unlinks; a drop on empty canvas does
/// nothing.</para>
///
/// <para>The delivery itself is deferred to <c>RhinoApp.Idle</c>: it writes into and expires a
/// document that is not the one being solved — the target sits on the user's canvas, this component
/// inside a harness — which cannot be done from within a solution.</para>
/// </summary>
public class HarnessOut : PhyBase, IHarnessOutlet, IGuidLinked
{
    private const int InData = 0;

    /// <summary>The name a fresh node's input — and so its grip on the proxy — starts out with.</summary>
    private const string DefaultName = "Data";

    private readonly TransmitterLink _link;

    // Identifies what was last transmitted, so ordinary dataflow delivers once per change rather than
    // on every solve (each delivery expires the target, and re-delivering the same values would keep
    // the canvas busy for nothing). See TreeIdentity for what the key is made of and why.
    private string? _lastKey;

    // The tree queued for the deferred write, and the problem the last one reported.
    private GH_Structure<IGH_Goo> _pending = new();
    private bool _idleHooked;
    private string? _warning;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessOut"/> class.
    /// </summary>
    public HarnessOut()
        : base(
            "Harness Out",
            "Harness Out",
            "Sends data out of the harness and into something on your canvas — a component input, a floating parameter, a panel. Drag the matching grip on the harness node onto the input it should feed, exactly as you would connect an ordinary output. Rename this node's input to label that grip.",
            "I/O")
    {
        _link = new TransmitterLink(this, "Component Input", "component input or panel", ParamTargets.CanHoldOrDisplay)
        {
            // A freshly linked target starts empty, so whatever is on the wire has to go in again.
            Changed = () =>
            {
                _lastKey = null;
                _warning = null;
            },
        };
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("2A7F51C6-08B4-4E39-9D62-C315E8B0A47F");

    /// <inheritdoc/>
    /// <remarks>
    /// Inside a harness the grip lives on the proxy, so this draws a plain node; standing alone on the
    /// canvas the same attribute grows the grip back onto the node itself.
    /// </remarks>
    public override void CreateAttributes()
    {
        m_attributes = new Attributes.OutletArrowAttrib(this, this);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The input's nickname, read live — so the grip on the proxy is labelled with whatever this node's
    /// input is called, and renaming the input relabels the grip. Read rather than copied because the
    /// proxy paints this label every frame; what a rename additionally has to do is expire the proxy's
    /// layout, so the strip reserved for the labels is re-measured for a longer name.
    /// </remarks>
    public string OutletLabel => Port?.NickName is { } nickname && !string.IsNullOrWhiteSpace(nickname)
        ? nickname
        : DefaultName;

    /// <inheritdoc/>
    public WireGradient OutletGradient => ArrowStyles.HarnessOut;

    /// <inheritdoc/>
    /// <remarks>
    /// Horizontal: this wire ends on the input grip it feeds, so it enters exactly as a Grasshopper
    /// wire carrying the same values would.
    /// </remarks>
    public bool HorizontalArrowEnd => true;

    /// <summary>
    /// Gets the InstanceGuid of the linked target, or <see cref="Guid.Empty"/> when unlinked.
    /// </summary>
    public Guid LinkedGuid => _link.Guid;

    /// <inheritdoc/>
    /// <remarks>
    /// The parameter is generic, so Grasshopper cannot know this node ever holds geometry and would
    /// give it no preview at all. Geometry is the heaviest thing it carries and the one thing a user
    /// looks for in the viewport, so the preview is supplied by hand from whatever on the input turns
    /// out to be drawable — the input, because there is no output to read.
    /// </remarks>
    public override bool IsPreviewCapable => true;

    /// <inheritdoc/>
    public override BoundingBox ClippingBox
    {
        get
        {
            BoundingBox box = BoundingBox.Empty;
            foreach (IGH_PreviewData data in Drawable())
            {
                box.Union(data.ClippingBox);
            }

            return box;
        }
    }

    // This node's input, typed so its rename can be heard. Null only while the component is being
    // constructed or has had its parameters torn down.
    private Param_HarnessPort? Port =>
        Params.Input.Count > InData ? Params.Input[InData] as Param_HarnessPort : null;

    /// <inheritdoc/>
    public IEnumerable<PointF> GetArrowEndpoints(GH_Document hostDocument) => _link.Endpoints(hostDocument);

    /// <inheritdoc/>
    /// <remarks>
    /// A component is entered through its inputs, the way a wire enters it: the input whose GRIP the
    /// drop landed nearest takes the link, falling back to the input row under the cursor and then to
    /// the node's first input. A drop on empty canvas does nothing.
    /// </remarks>
    public void HandleDrop(GH_Document hostDocument, PointF dropPoint, bool ctrl) =>
        _link.HandleDrop(
            hostDocument,
            dropPoint,
            ctrl,
            (hit, point) => ParamTargets.RefineDropTarget(hit, point, ParamTargets.CanHoldOrDisplay));

    /// <inheritdoc/>
    /// <remarks>
    /// Drops the linked input along with the delivery bookkeeping about it — the last-written key and
    /// any warning are statements about a target this outlet no longer has.
    /// </remarks>
    public void ClearHostTarget()
    {
        _link.Assign(Guid.Empty);
        _lastKey = null;
        _warning = null;
    }

    /// <inheritdoc/>
    /// <remarks>Offers the link as a menu too, for a target a drag cannot conveniently reach.</remarks>
    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);
        _link.AppendMenuItems(menu);
    }

    /// <inheritdoc/>
    void IGuidLinked.RemapLinks(IReadOnlyDictionary<Guid, Guid> replacements) => _link.Remap(replacements);

    /// <inheritdoc/>
    public override bool Write(GH_IWriter writer)
    {
        _link.Write(writer);
        return base.Write(writer);
    }

    /// <inheritdoc/>
    public override bool Read(GH_IReader reader)
    {
        _link.Read(reader);
        return base.Read(reader);
    }

    /// <inheritdoc/>
    /// <remarks>Re-binds the input's rename callback, which a parameter restored from an archive lacks.</remarks>
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);

        if (Port is { } port)
        {
            BindPort(port);
        }
    }

    /// <inheritdoc/>
    public override void RemovedFromDocument(GH_Document document)
    {
        UnhookIdle();
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
        var wire = new GH_PreviewWireArgs(
            args.Viewport,
            args.Display,
            Attributes.Selected ? args.WireColour_Selected : args.WireColour,
            args.DefaultCurveThickness);

        foreach (IGH_PreviewData data in Drawable())
        {
            data.DrawViewportWires(wire);
        }
    }

    /// <inheritdoc/>
    public override void DrawViewportMeshes(IGH_PreviewArgs args)
    {
        var mesh = new GH_PreviewMeshArgs(
            args.Viewport,
            args.Display,
            Attributes.Selected ? args.ShadeMaterial_Selected : args.ShadeMaterial,
            args.MeshingParameters);

        foreach (IGH_PreviewData data in Drawable())
        {
            data.DrawViewportMeshes(mesh);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Tree access, not item or list: the branching is data, and it is carried out to the target
    /// unchanged.
    /// </remarks>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // Nicknamed in full rather than initialled, because this name is painted beside the grip on
        // the harness proxy and has to read as a label out there. Bound here as well as in
        // AddedToDocument: this runs during construction, so a rename is heard from the very first one.
        var port = new Param_HarnessPort();
        BindPort(port);

        pManager.AddParameter(
            port,
            DefaultName,
            DefaultName,
            "Anything at all: geometry, numbers, text, booleans, even a signal. It is written into the linked input with its branch structure intact. Rename it and the grip on the harness node is relabelled to match.",
            GH_ParamAccess.tree);
        pManager[InData].Optional = true;
    }

    /// <inheritdoc/>
    /// <remarks>None: this is where the pipeline ends and the user's canvas takes over.</remarks>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Reported from the deferred write, which has no solve of its own to speak from.
        if (_warning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _warning);
        }

        if (!DA.GetDataTree(InData, out GH_Structure<IGH_Goo> tree) || tree is null || tree.IsEmpty)
        {
            // Nothing on the wire. Forget the last transmission so the same data arriving later still
            // counts as new and is transmitted again.
            _lastKey = null;
            return;
        }

        string key = TreeIdentity.Of(tree, ItemKey);
        if (key == _lastKey)
        {
            return;
        }

        _lastKey = key;
        QueueWrite(tree);
    }

    // What makes one item that item. Reference identity for ordinary data, which is exact in the
    // direction that matters (see TreeIdentity) — but NOT for a signal, which is re-wrapped in a fresh
    // goo on every single solve of the pipeline. Identity there would report a change every time and
    // have this writing to the canvas on every scheduled solve; the sequence is what makes a signal
    // that signal, and two signals carrying the same payload are still two transmissions.
    private static string ItemKey(IGH_Goo? item) => item switch
    {
        null => "0",
        GH_Signal { Value: { } signal } => $"#{signal.Sequence}",
        _ => RuntimeHelpers.GetHashCode(item).ToString(),
    };

    // Whatever is currently on the input that Rhino knows how to draw. Empty while the node's preview
    // is off or it is locked, so the context-menu toggle behaves as it does on any other component.
    private IEnumerable<IGH_PreviewData> Drawable()
    {
        if (Hidden || Locked || Params.Input.Count <= InData)
        {
            yield break;
        }

        foreach (IGH_Goo? goo in Params.Input[InData].VolatileData.AllData(true))
        {
            if (goo is IGH_PreviewData data)
            {
                yield return data;
            }
        }
    }

    // Points the input at this node, so renaming it relabels the grip on the proxy. The label itself is
    // read live off OutletLabel and needs no pushing; what this is for is the LAYOUT, since the strip
    // the proxy reserves for its labels is measured at layout time and a longer name needs more of it.
    private void BindPort(Param_HarnessPort port)
    {
        port.Renamed = name =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                // A cleared name is taken as "back to the default" rather than obeyed: an unlabelled
                // grip on the proxy tells the user nothing.
                port.NickName = DefaultName;
                return;
            }

            PhyDocuments.Harness(this)?.OnOutletRenamed();
        };
    }

    // Queues the deferred write. Delivery mutates and expires a document that is not the one being
    // solved, so it waits for the solution to settle; RhinoApp.Idle fires on the UI thread.
    private void QueueWrite(GH_Structure<IGH_Goo> tree)
    {
        // Copied, because the structure handed out by the solve belongs to the input parameter and is
        // cleared the moment that parameter expires — which the very next solve does, well before the
        // idle callback runs. Shallow: the goo inside is immutable as far as this component is
        // concerned, and it is the same goo that arrived.
        _pending = new GH_Structure<IGH_Goo>(tree, false);

        if (_idleHooked)
        {
            return;
        }

        _idleHooked = true;
        Rhino.RhinoApp.Idle += OnIdleWrite;
    }

    private void UnhookIdle()
    {
        if (_idleHooked)
        {
            Rhino.RhinoApp.Idle -= OnIdleWrite;
            _idleHooked = false;
        }
    }

    private void OnIdleWrite(object? sender, EventArgs e)
    {
        UnhookIdle();

        string? previous = _warning;
        try
        {
            _warning = Deliver(_pending);
        }
        catch (Exception ex)
        {
            _warning = $"The data could not be transmitted: {ex.Message}";
        }

        // Re-solve only to change what the node says: without a Fail Signal to carry the news, the
        // runtime message is the only place a delivery problem can surface. The data on the wire is
        // unchanged by now, so this can never queue another write.
        if (_warning != previous)
        {
            ExpireSolution(true);
        }
    }

    // Writes the data into the linked target. Returns null on success, or what went wrong.
    private string? Deliver(GH_Structure<IGH_Goo> tree)
    {
        IGH_DocumentObject? target = _link.Resolve(out string? linkError);
        if (target is null)
        {
            return linkError;
        }

        // A Panel is the target a user reaches for to SEE what is going out. It holds one string and
        // splits it by line, so the items land as a LIST — each one cast to text on its own, the way a
        // wire would have delivered them. Branching is the one thing it cannot carry.
        if (target is GHPanel panel)
        {
            ParamTargets.WritePanel(panel, tree);
            panel.ExpireSolution(true);

            return tree.PathCount > 1
                ? $"\"{panel.NickName}\" is a Panel, which holds one list — the {tree.PathCount} branches "
                    + "were flattened into it. Transmit into a Text parameter to keep the tree."
                : null;
        }

        if (target is not IGH_Param param)
        {
            return $"The linked object cannot hold data ({target.GetType().Name}).";
        }

        if (ParamTargets.WriteTree(param, tree, out int rejected) is { } writeError)
        {
            return writeError;
        }

        param.ExpireSolution(true);

        // Internalised data loses to a wire every time, so the values would be delivered and then
        // silently overridden — exactly the "nothing happened" this component must never present
        // without explanation.
        if (param.SourceCount > 0)
        {
            return $"\"{param.NickName}\" has a wire into it, which overrides the transmitted data. "
                + "Disconnect that wire for this transmitter to drive it.";
        }

        if (rejected > 0)
        {
            int total = tree.DataCount;
            return rejected == total
                ? $"\"{param.NickName}\" could not read any of the {total} transmitted item(s) as {param.TypeName}."
                : $"\"{param.NickName}\" could not read {rejected} of {total} transmitted item(s) as {param.TypeName}.";
        }

        // A cast the target could not make leaves it empty — say so, rather than let the user hunt for
        // data that was delivered but discarded.
        return ParamTargets.DeliveredCount(param) == 0 && tree.DataCount > 0
            ? $"\"{param.NickName}\" could not read the transmitted data as {param.TypeName}."
            : null;
    }
}
