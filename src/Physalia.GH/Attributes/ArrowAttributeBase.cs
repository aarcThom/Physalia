// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.GH.Attributes.UiElements;

namespace Physalia.GH.Attributes;

/// <summary>
/// Base attributes for a component whose bottom grip drives a single bezier drag arrow. Adds the
/// shared <see cref="ArrowGrip"/> wire rendering and drag state machine on top of
/// <see cref="BottomGripAttributes"/>: subclasses supply only the arrow's colour, endpoints, and
/// drop behaviour (through <see cref="IArrowHost"/>), and optionally a custom drag hit test via
/// <see cref="TryStartDrag"/>. The link attributes and <see cref="OutletArrowAttrib"/> derive from
/// this; the harness proxy reuses the same <see cref="ArrowGrip"/> by composition instead, because
/// it hosts one arrow per outlet rather than a single one.
/// </summary>
public abstract class ArrowAttributeBase : BottomGripAttributes, IArrowHost
{
    private readonly ArrowGrip _arrow = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrowAttributeBase"/> class.
    /// </summary>
    /// <param name="component">The component that owns these attributes.</param>
    protected ArrowAttributeBase(IGH_Component component)
        : base(component)
    {
    }

    /// <inheritdoc/>
    /// <remarks>Wherever the grip sits — bottom-centre unless the subclass moved it.</remarks>
    public PointF ArrowOrigin => GripOrigin;

    /// <inheritdoc/>
    public abstract WireGradient ArrowGradient { get; }

    /// <inheritdoc/>
    public virtual IArrowHead ArrowHead => TriangleArrowHead.Default;

    /// <inheritdoc/>
    public virtual bool HorizontalArrow => false;

    /// <inheritdoc/>
    /// <remarks>
    /// Follows the departure unless a subclass says otherwise: a single-arrow component's two ends
    /// normally agree, and this keeps the common case one override rather than two.
    /// </remarks>
    public virtual bool HorizontalArrowEnd => HorizontalArrow;

    /// <summary>Gets the shared arrow controller, for subclass hit tests that start a drag.</summary>
    protected ArrowGrip Arrow => _arrow;

    /// <inheritdoc/>
    public abstract IEnumerable<PointF> SettledEndpoints(GH_Document doc);

    /// <inheritdoc/>
    public abstract void OnDrop(GH_Document doc, PointF dropPoint, bool ctrl);

    /// <inheritdoc/>
    protected override void RenderGripContent(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (channel == GH_CanvasChannel.Wires)
        {
            _arrow.DrawWires(graphics, canvas.Document, this);
        }
    }

    /// <inheritdoc/>
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button == MouseButtons.Left && TryStartDrag(sender, e))
        {
            return GH_ObjectResponse.Capture;
        }

        return base.RespondToMouseDown(sender, e);
    }

    /// <inheritdoc/>
    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_arrow.IsDragging)
        {
            _arrow.UpdateDrag(sender, e.CanvasLocation);
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseMove(sender, e);
    }

    /// <inheritdoc/>
    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_arrow.IsDragging)
        {
            _arrow.EndDrag(sender, sender.Document, e.CanvasLocation, this);
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseUp(sender, e);
    }

    /// <summary>
    /// Decides whether a left-button press starts a drag, and starts it if so. The default begins a
    /// drag only when the press lands on the grip itself (<see cref="GripHitRegion"/>), carrying the
    /// Ctrl (disconnect) intent. Override to add alternative hit zones (e.g. grabbing an existing
    /// arrow tip).
    ///
    /// <para>Deliberately NOT <see cref="GripBounds"/>, which is the whole node: testing that made
    /// every press anywhere on the component pull out a wire, so the component could not be dragged
    /// around the canvas at all.</para>
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>true if a drag was started; otherwise false.</returns>
    protected virtual bool TryStartDrag(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (GripHitRegion.Contains(e.CanvasLocation))
        {
            bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            _arrow.StartDrag(sender, e.CanvasLocation, ctrl);
            return true;
        }

        return false;
    }
}
