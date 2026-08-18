// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Goo;
using Physalia.GH.Harness;
using GHPanel = Grasshopper.Kernel.Special.GH_Panel;

namespace Physalia.GH.Components;

/// <summary>
/// Carries text out of the harness, onto the user's canvas, and is a plain passthrough on the way.
///
/// <para><b>One input, one output.</b> <b>Data In</b> takes either a signal or text — whichever
/// arrives leaves again on <b>Data Out</b>, untouched. A signal passes through as the SAME signal
/// (same sequence), so downstream still consumes it exactly once and the pipeline reads as if this
/// component were not there; text passes through as text. There is no Success/Fail pair, no latching,
/// no consume-once bookkeeping: this component decides nothing, so it has no state machine.</para>
///
/// <para><b>What it transmits</b> is the text form of whatever came in: a signal's payload, or the
/// text itself. It goes out through the harness proxy's "text" grip, which behaves like an ordinary
/// Grasshopper output — drag it onto the <b>input grip</b> downstream that should receive the value
/// and it stays connected, the wire drawn to that grip and the value delivered on every change. Any
/// input will do, not only a text one: the value is cast into whatever the input holds, exactly as a
/// wire's would be. Ctrl+drop on the target unlinks; a drop on empty canvas does nothing.</para>
///
/// <para>The delivery itself is deferred to <c>RhinoApp.Idle</c>: it writes into and expires a
/// document that is not the one being solved — the target sits on the user's canvas, this component
/// inside a harness — which cannot be done from within a solution.</para>
/// </summary>
public class TextTransmitter : PhyBase, IHarnessOutlet, IGuidLinked
{
    private const int InData = 0;
    private const int OutData = 0;

    private readonly TransmitterLink _link;

    // Identifies what was last transmitted, so ordinary dataflow delivers once per change rather than
    // on every solve (each delivery expires the target, and re-delivering unchanged text would keep
    // the canvas busy for nothing). Keyed by IDENTITY where there is one — two different signals with
    // the same payload are two transmissions.
    private string? _lastKey;

    // The text queued for the deferred write, and the problem the last one reported.
    private string _pending = string.Empty;
    private bool _idleHooked;
    private string? _warning;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextTransmitter"/> class.
    /// </summary>
    public TextTransmitter()
        : base(
            "Text Transmitter",
            "TextTx",
            "Passes whatever arrives — a signal or text — straight through, and transmits its text form out of the harness into a linked component input, parameter, or panel. Drag the harness's \"text\" grip onto the input grip it should feed, the way an ordinary Grasshopper output connects.",
            "Transmitters")
    {
        _link = new TransmitterLink(this, "Component Input", "component input or panel", CanReceive)
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
    public override Guid ComponentGuid => new Guid("B7D4E9F1-3C60-4A2E-95B8-1F0C7A6D2E43");

    /// <inheritdoc/>
    /// <remarks>
    /// Inside a harness the "text" grip lives on the proxy, so this draws a plain node; standing
    /// alone on the canvas the same attribute grows the grip back onto the node itself.
    /// </remarks>
    public override void CreateAttributes()
    {
        m_attributes = new Attributes.OutletArrowAttrib(this, this);
    }

    /// <inheritdoc/>
    public string OutletLabel => "text";

    /// <inheritdoc/>
    public WireGradient OutletGradient => ArrowStyles.TextTx;

    /// <inheritdoc/>
    /// <remarks>
    /// Horizontal: this wire ends on the input grip it feeds, so it enters exactly as a Grasshopper
    /// wire carrying the same value would.
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
            (hit, point) => ParamTargets.RefineDropTarget(hit, point, CanReceive));

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
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter(
            "Data In",
            "D",
            "A signal or plain text. Whichever arrives passes straight through to Data Out; its text form is what gets transmitted out of the harness.",
            GH_ParamAccess.item);
        pManager[InData].Optional = true;
    }

    /// <inheritdoc/>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter(
            "Data Out",
            "D",
            "Exactly what arrived on Data In — the same signal (same sequence, so downstream still consumes it once), or the same text.",
            GH_ParamAccess.item);
    }

    /// <inheritdoc/>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        // Reported from the deferred write, which has no solve of its own to speak from.
        if (_warning != null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _warning);
        }

        IGH_Goo? goo = null;
        if (!DA.GetData(InData, ref goo) || goo is null)
        {
            // Nothing on the wire. Forget the last transmission so the same value arriving later
            // still counts as new and is transmitted again.
            _lastKey = null;
            return;
        }

        // The passthrough, first and unconditionally: whatever this component's own delivery does,
        // the pipeline behind it must not be held up.
        DA.SetData(OutData, goo);

        if (!TryGetText(goo, out string text, out string key) || key == _lastKey)
        {
            return;
        }

        _lastKey = key;
        QueueWrite(text);
    }

    /// <summary>
    /// Whether an object on the host canvas can receive the transmitted value: a Panel, or any
    /// parameter that can hold data of its own — which is what a component's input IS, so this covers
    /// inputs of every type, not only text ones. The value is cast into whatever the input holds,
    /// exactly as a wire's would be.
    /// </summary>
    /// <param name="candidate">An object on the user's canvas.</param>
    /// <returns>true when a value can be delivered into it.</returns>
    private static bool CanReceive(IGH_DocumentObject candidate) =>
        candidate is GHPanel || ParamTargets.CanHold(candidate);

    // The text a piece of goo transmits, plus the key that decides whether it is new. A signal is
    // keyed by its sequence — the one thing that makes it that signal — and everything else by its
    // own text, since that is all it is.
    private static bool TryGetText(IGH_Goo goo, out string text, out string key)
    {
        if (goo is GH_Signal { Value: { } signal })
        {
            text = signal.Payload;
            key = $"#{signal.Sequence}";
            return true;
        }

        if (GH_Convert.ToString(goo, out string converted, GH_Conversion.Both))
        {
            text = converted;
            key = "=" + converted;
            return true;
        }

        text = string.Empty;
        key = string.Empty;
        return false;
    }

    // Queues the deferred write. Delivery mutates and expires a document that is not the one being
    // solved, so it waits for the solution to settle; RhinoApp.Idle fires on the UI thread.
    private void QueueWrite(string text)
    {
        _pending = text;

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
            _warning = $"The text could not be transmitted: {ex.Message}";
        }

        // Re-solve only to change what the node says: without a Fail Signal to carry the news, the
        // runtime message is the only place a delivery problem can surface. The value on the wire is
        // unchanged by now, so this can never queue another write.
        if (_warning != previous)
        {
            ExpireSolution(true);
        }
    }

    // Writes the text into the linked target. Returns null on success, or what went wrong.
    private string? Deliver(string text)
    {
        IGH_DocumentObject? target = _link.Resolve(out string? linkError);
        if (target is null)
        {
            return linkError;
        }

        if (target is GHPanel panel)
        {
            panel.SetUserText(text);
            panel.ExpireSolution(true);
            return null;
        }

        if (target is not IGH_Param param || ParamTargets.PersistentSetter(param) is not { } setter)
        {
            return $"The linked object cannot hold a value ({target.GetType().Name}).";
        }

        // params object[] through reflection: one argument, itself an object[].
        setter.Invoke(param, new object[] { new object[] { text } });
        param.ExpireSolution(true);

        // Internalised data loses to a wire every time, so the value would be delivered and then
        // silently overridden — exactly the "nothing happened" this component must never present
        // without explanation.
        if (param.SourceCount > 0)
        {
            return $"\"{param.NickName}\" has a wire into it, which overrides the transmitted value. "
                + "Disconnect that wire for this transmitter to drive it.";
        }

        // A cast the target could not make leaves it empty — say so, rather than let the user hunt for
        // a value that was delivered but discarded.
        return ParamTargets.DeliveredCount(param) == 0 && text.Length > 0
            ? $"\"{param.NickName}\" could not read \"{Truncate(text)}\" as {param.TypeName}."
            : null;
    }

    private static string Truncate(string text) =>
        text.Length <= 30 ? text : text[..30] + "…";
}
