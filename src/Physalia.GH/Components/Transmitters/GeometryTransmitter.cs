// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Harness;
using GHPanel = Grasshopper.Kernel.Special.GH_Panel;

namespace Physalia.GH.Components;

/// <summary>
/// Carries geometry out of the harness, onto the user's canvas, and is a plain passthrough on the way.
///
/// <para><b>One input, one output.</b> Whatever arrives on <b>Geometry In</b> leaves again on
/// <b>Geometry Out</b>, untouched and — this is the point of the component — with its TREE INTACT:
/// the same items on the same paths, in and out and onward into the linked target. There is no
/// Success/Fail pair, no latching, no consume-once bookkeeping: this component decides nothing, so
/// it has no state machine. It is the geometry counterpart of <see cref="TextTransmitter"/> and is
/// built the same way, sharing the target/delivery mechanism through <see cref="ParamTargets"/>.</para>
///
/// <para><b>Any geometry, any input.</b> The parameters are Grasshopper's own Geometry type, so
/// every geometric kind rides through — points, curves, breps, meshes, SubDs, planes, boxes. It goes
/// out through the harness proxy's "geo" grip, which behaves like an ordinary Grasshopper output:
/// drag it onto the <b>input grip</b> downstream that should receive the geometry and it stays
/// connected, the wire drawn to that grip and the values delivered on every change. Any input will
/// do, not only a geometry one — the values are cast into whatever the input holds, exactly as a
/// wire's would be — and an input that cannot read them says so rather than going quietly empty.
/// Ctrl+drop on the target unlinks; a drop on empty canvas does nothing.</para>
///
/// <para><b>Text targets work too, PER ITEM.</b> Stringifying never collapses the data: a text
/// parameter casts each piece of geometry to text on its own and keeps the tree it arrived in, and
/// an item a target refuses outright is offered again as its own text form for the same reason. A
/// Panel gets the items one per line — a LIST of the same length, not one blob — which is the only
/// shape a Panel's single string can be read back as; branching is the one thing it cannot carry, and
/// a tree sent to one is flattened with a warning that says so.</para>
///
/// <para>The delivery itself is deferred to <c>RhinoApp.Idle</c>: it writes into and expires a
/// document that is not the one being solved — the target sits on the user's canvas, this component
/// inside a harness — which cannot be done from within a solution.</para>
/// </summary>
public class GeometryTransmitter : PhyBase, IHarnessOutlet, IGuidLinked
{
    private const int InGeometry = 0;
    private const int OutGeometry = 0;

    private readonly TransmitterLink _link;

    // Identifies what was last transmitted, so ordinary dataflow delivers once per change rather than
    // on every solve (each delivery expires the target, and re-delivering the same geometry would keep
    // the canvas busy for nothing). See TreeIdentity for what the key is made of and why.
    private string? _lastKey;

    // The tree queued for the deferred write, and the problem the last one reported.
    private GH_Structure<IGH_GeometricGoo> _pending = new();
    private bool _idleHooked;
    private string? _warning;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeometryTransmitter"/> class.
    /// </summary>
    public GeometryTransmitter()
        : base(
            "Geometry Transmitter",
            "GeoTx",
            "Passes geometry straight through, tree structure and all, and transmits it out of the harness into a linked component input, parameter, or panel. Drag the harness's \"geo\" grip onto the input grip it should feed, the way an ordinary Grasshopper output connects.",
            "Transmitters")
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
    public override Guid ComponentGuid => new Guid("9494B0DF-81FC-46CC-A295-B92C1F96F5FD");

    /// <inheritdoc/>
    /// <remarks>
    /// Inside a harness the "geo" grip lives on the proxy, so this draws a plain node; standing
    /// alone on the canvas the same attribute grows the grip back onto the node itself.
    /// </remarks>
    public override void CreateAttributes()
    {
        m_attributes = new Attributes.OutletArrowAttrib(this, this);
    }

    /// <inheritdoc/>
    public string OutletLabel => "geo";

    /// <inheritdoc/>
    public WireGradient OutletGradient => ArrowStyles.GeoTx;

    /// <inheritdoc/>
    /// <remarks>
    /// Horizontal: this wire ends on the input grip it feeds, so it enters exactly as a Grasshopper
    /// wire carrying the same geometry would.
    /// </remarks>
    public bool HorizontalArrowEnd => true;

    /// <summary>
    /// Gets the InstanceGuid of the linked target, or <see cref="Guid.Empty"/> when unlinked.
    /// </summary>
    public Guid LinkedGuid => _link.Guid;

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
    public override void RemovedFromDocument(GH_Document document)
    {
        UnhookIdle();
        base.RemovedFromDocument(document);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Tree access, not item or list: the branching is data, and this component's whole promise is
    /// that it does not touch it.
    /// </remarks>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGeometryParameter(
            "Geometry In",
            "G",
            "Any Grasshopper geometry. It passes straight through to Geometry Out, and is transmitted out of the harness into the linked input — tree structure intact in both directions.",
            GH_ParamAccess.tree);
        pManager[InGeometry].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGeometryParameter(
            "Geometry Out",
            "G",
            "Exactly what arrived on Geometry In — the same items on the same paths.",
            GH_ParamAccess.tree);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Reported from the deferred write, which has no solve of its own to speak from.
        if (_warning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _warning);
        }

        if (!DA.GetDataTree(InGeometry, out GH_Structure<IGH_GeometricGoo> tree) || tree.IsEmpty)
        {
            // Nothing on the wire. Forget the last transmission so the same geometry arriving later
            // still counts as new and is transmitted again.
            _lastKey = null;
            return;
        }

        // The passthrough, first and unconditionally: whatever this component's own delivery does,
        // the pipeline behind it must not be held up.
        DA.SetDataTree(OutGeometry, tree);

        string key = TreeIdentity.Of(tree);
        if (key == _lastKey)
        {
            return;
        }

        _lastKey = key;
        QueueWrite(tree);
    }

    // Queues the deferred write. Delivery mutates and expires a document that is not the one being
    // solved, so it waits for the solution to settle; RhinoApp.Idle fires on the UI thread.
    private void QueueWrite(GH_Structure<IGH_GeometricGoo> tree)
    {
        // Copied, because the structure handed out by the solve belongs to the input parameter and is
        // cleared the moment that parameter expires — which the very next solve does, well before the
        // idle callback runs. Shallow: the goo inside is immutable as far as this component is
        // concerned, and it is the same goo the passthrough sent on.
        _pending = new GH_Structure<IGH_GeometricGoo>(tree, false);

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
            _warning = $"The geometry could not be transmitted: {ex.Message}";
        }

        // Re-solve only to change what the node says: without a Fail Signal to carry the news, the
        // runtime message is the only place a delivery problem can surface. The geometry on the wire
        // is unchanged by now, so this can never queue another write.
        if (_warning != previous)
        {
            ExpireSolution(true);
        }
    }

    // Writes the geometry into the linked target. Returns null on success, or what went wrong.
    private string? Deliver(GH_Structure<IGH_GeometricGoo> tree)
    {
        IGH_DocumentObject? target = _link.Resolve(out string? linkError);
        if (target is null)
        {
            return linkError;
        }

        // A Panel is the target a user reaches for to SEE what is going out. It holds one string and
        // splits it by line, so the items land as a LIST — each piece of geometry cast to text on its
        // own, the way a wire would have delivered them. Branching is the one thing it cannot carry.
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
            return $"The linked object cannot hold geometry ({target.GetType().Name}).";
        }

        if (ParamTargets.WriteTree(param, tree, out int rejected) is { } writeError)
        {
            return writeError;
        }

        param.ExpireSolution(true);

        // Internalised data loses to a wire every time, so the geometry would be delivered and then
        // silently overridden — exactly the "nothing happened" this component must never present
        // without explanation.
        if (param.SourceCount > 0)
        {
            return $"\"{param.NickName}\" has a wire into it, which overrides the transmitted geometry. "
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
        // geometry that was delivered but discarded.
        return ParamTargets.DeliveredCount(param) == 0 && tree.DataCount > 0
            ? $"\"{param.NickName}\" could not read the transmitted geometry as {param.TypeName}."
            : null;
    }
}
