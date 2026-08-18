// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Harness;

namespace Physalia.GH.Attributes;

/// <summary>
/// A transmitter's own drag arrow, for when it is NOT inside a harness.
///
/// <para>A transmitter is normally a harness outlet: it lives in the sub-document while the things
/// its arrow points at live on the user's canvas, so the arrow is hosted by the proxy and this
/// attribute draws nothing (see <see cref="IHarnessOutlet"/> and <c>HarnessAttrib</c>). Placed
/// straight onto the canvas, though, the transmitter and its targets share one document and the
/// gesture has nowhere else to live — so the grip comes back onto the node itself, exactly as it
/// worked before harnesses existed.</para>
///
/// <para>One attribute covers both cases rather than two classes chosen at construction time,
/// because attributes are built before the component reaches a document: residency is read live,
/// per layout and per frame, from the document the component actually ended up on.</para>
///
/// <para>The grip sits at the bottom, not on the right edge where the proxy puts it: a transmitter
/// is a routing component whose right edge already carries its Signal outputs.</para>
/// </summary>
public sealed class OutletArrowAttrib : ArrowAttributeBase
{
    private readonly IHarnessOutlet _outlet;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutletArrowAttrib"/> class.
    /// </summary>
    /// <param name="component">The component that owns these attributes.</param>
    /// <param name="outlet">The outlet supplying the arrow's colour, endpoints and drop behaviour — normally the same object.</param>
    public OutletArrowAttrib(IGH_Component component, IHarnessOutlet outlet)
        : base(component)
    {
        ArgumentNullException.ThrowIfNull(outlet);
        _outlet = outlet;
    }

    /// <inheritdoc/>
    public override WireGradient ArrowGradient => _outlet.OutletGradient;

    /// <inheritdoc/>
    /// <remarks>The arrival is the outlet's own call — see <see cref="IHarnessOutlet"/>.</remarks>
    public override bool HorizontalArrowEnd => _outlet.HorizontalArrowEnd;

    // True when this transmitter is on a plain document rather than in a harness, and so owns its
    // own arrow. Read live: the answer is a property of where the component ended up, not of when
    // its attributes were made.
    private bool OwnsArrow => !PhyDocuments.IsHarnessDocument(Owner?.OnPingDocument());

    // The document the outlet writes into. Standing alone that is simply the component's own
    // document; falling back to the canvas keeps the arrow drawable during the frame a component is
    // being added or removed.
    private GH_Document? TargetDocument(GH_Document? canvasDocument) =>
        Owner?.OnPingDocument() ?? canvasDocument;

    /// <inheritdoc/>
    public override IEnumerable<PointF> SettledEndpoints(GH_Document doc) =>
        OwnsArrow && TargetDocument(doc) is { } target
            ? _outlet.GetArrowEndpoints(target)
            : Array.Empty<PointF>();

    /// <inheritdoc/>
    public override void OnDrop(GH_Document doc, PointF dropPoint, bool ctrl)
    {
        if (OwnsArrow && TargetDocument(doc) is { } target)
        {
            _outlet.HandleDrop(target, dropPoint, ctrl);
        }
    }

    /// <inheritdoc/>
    /// <remarks>No grip inside a harness, so no strip to make one hittable either.</remarks>
    protected override RectangleF ExpandForGrip(RectangleF visual) =>
        OwnsArrow ? base.ExpandForGrip(visual) : visual;

    /// <inheritdoc/>
    protected override void DrawGrip(Graphics graphics, PointF origin)
    {
        if (OwnsArrow)
        {
            base.DrawGrip(graphics, origin);
        }
    }

    /// <inheritdoc/>
    protected override void RenderGripContent(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (OwnsArrow)
        {
            base.RenderGripContent(canvas, graphics, channel);
        }
    }

    /// <inheritdoc/>
    protected override bool TryStartDrag(GH_Canvas sender, GH_CanvasMouseEvent e) =>
        OwnsArrow && base.TryStartDrag(sender, e);
}
