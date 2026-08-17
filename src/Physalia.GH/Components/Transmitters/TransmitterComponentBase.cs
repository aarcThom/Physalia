// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Physalia.Core.Common;
using Physalia.Core.Signals;
using Physalia.GH.Attributes;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Harness;

namespace Physalia.GH.Components;

/// <summary>
/// Base for SIGNAL-DRIVEN transmitters: the components that take an LLM-generated payload off the
/// signal and write it OUT of the harness — onto the user's canvas, or into a component sitting on
/// it. A transmitter is the harness's one kind of output (see <see cref="IHarnessOutlet"/>), so the
/// proxy grows a grip for each one inside it.
///
/// <para>What such a transmitter shares lives here: the payload comes off the consumed signal, the
/// node itself is drawn plain (the drag arrow belongs to the proxy, on the canvas the targets are
/// on), and the arrow's coordinates are measured from that proxy. What differs — the label and
/// colour of the grip, where its settled wires land, what a drop means, and of course the push
/// itself — is left to the subclass. Those that push into an EXISTING component on the canvas share
/// a second tier, <see cref="ScriptTransmitterBase"/>.</para>
///
/// <para>Not every transmitter belongs here. <see cref="TextTransmitter"/> is driven by ordinary
/// dataflow rather than by signals — one input, one output, no routing — so it implements
/// <see cref="IHarnessOutlet"/> directly and composes a <see cref="TransmitterLink"/> for the linked
/// target. The outlet is the contract; this class is only the common case.</para>
/// </summary>
public abstract class TransmitterComponentBase : RoutingComponentBase<string>, IHarnessOutlet
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TransmitterComponentBase"/> class in the
    /// Transmitters section, where every transmitter belongs.
    /// </summary>
    /// <param name="name">Component display name.</param>
    /// <param name="nickname">Component nickname.</param>
    /// <param name="description">Component description.</param>
    protected TransmitterComponentBase(string name, string nickname, string description)
        : base(name, nickname, description, "Transmitters")
    {
    }

    /// <inheritdoc/>
    public abstract string OutletLabel { get; }

    /// <inheritdoc/>
    public abstract WireGradient OutletGradient { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Horizontal by default — a transmitter whose wire ends where data would go arrives there the
    /// way a wire does. One that only points at a whole component overrides it.
    /// </remarks>
    public virtual bool HorizontalArrowEnd => true;

    /// <summary>
    /// Gets the canvas point this transmitter's arrow geometry is measured from: the harness
    /// proxy's pivot, because that is the node the arrow is drawn from and it shares a coordinate
    /// space with the drop point. Falls back to this component's own pivot when it is not in a
    /// harness (the arrow is then unreachable, but an offset stored before the move still resolves
    /// sensibly).
    /// </summary>
    protected PointF ArrowAnchor =>
        HarnessComponent.OwnerOf(OnPingDocument())?.Attributes?.Pivot ?? Attributes.Pivot;

    /// <inheritdoc/>
    /// <remarks>
    /// A plain node: the drag arrow lives on the harness proxy, which sits on the canvas this
    /// transmitter writes to (see <see cref="IHarnessOutlet"/>).
    /// </remarks>
    public override void CreateAttributes()
    {
        m_attributes = new PhyComponentAttributes(this);
    }

    /// <inheritdoc/>
    public abstract IEnumerable<PointF> GetArrowEndpoints(GH_Document hostDocument);

    /// <inheritdoc/>
    public abstract void HandleDrop(GH_Document hostDocument, PointF dropPoint, bool ctrl);

    /// <inheritdoc/>
    /// <remarks>
    /// Every transmitter takes its payload straight off the consumed signal — the generated
    /// document, script, or text to write out.
    /// </remarks>
    protected override bool TryGetData(PhySignal signal, IGH_DataAccess da, out string data)
    {
        data = signal.Payload;
        return StringHelpers.IsNonBlank(data);
    }
}
