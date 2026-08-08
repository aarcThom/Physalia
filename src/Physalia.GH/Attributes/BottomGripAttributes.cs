// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.GH.Attributes.UiElements;

namespace Physalia.GH.Attributes;

/// <summary>
/// Base attributes for a Physalia component that carries a single bottom-centre grip handle.
/// Expands the pick region downward so the grip is hittable, draws the grip in the Objects
/// channel, and honours the harness collapse state (inherited from
/// <see cref="PhyComponentAttributes"/>). Subclasses that need more than a static grip — e.g. a
/// drag arrow — override <see cref="RenderGripContent"/>; the wire/drag mechanics live in
/// <see cref="ArrowAttributeBase"/> on top of this.
/// </summary>
public abstract class BottomGripAttributes : PhyComponentAttributes
{
    /// <summary>Downward pick-region expansion (canvas units) that makes the bottom grip hittable.</summary>
    protected const float GripExpansion = 10f;

    private readonly CanvasGrip _grip = new(PointF.Empty);

    private RectangleF _visualBounds;
    private RectangleF _gripBounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="BottomGripAttributes"/> class.
    /// </summary>
    /// <param name="component">The component that owns these attributes.</param>
    protected BottomGripAttributes(IGH_Component component)
        : base(component)
    {
    }

    /// <summary>Gets the un-expanded capsule bounds, where the component and grip draw.</summary>
    protected RectangleF VisualBounds => _visualBounds;

    /// <summary>Gets the expanded pick region (the capsule plus the bottom grip strip).</summary>
    protected RectangleF GripBounds => _gripBounds;

    /// <summary>Gets the bottom-centre of the visible capsule — the grip origin / wire start.</summary>
    protected PointF BottomCentre =>
        new(_visualBounds.Left + (_visualBounds.Width / 2f), _visualBounds.Y + _visualBounds.Height);

    /// <inheritdoc/>
    protected override void Layout()
    {
        base.Layout();

        _visualBounds = Bounds;
        _gripBounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + GripExpansion);
        Bounds = _gripBounds;
    }

    /// <inheritdoc/>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        // The component and grip draw against the un-expanded bounds; restore the pick region after.
        RectangleF outer = Bounds;
        Bounds = _visualBounds;

        if (channel == GH_CanvasChannel.Objects)
        {
            DrawGrip(graphics, BottomCentre);
        }

        RenderGripContent(canvas, graphics, channel);

        // PhyComponentAttributes.Render draws the component capsule (the collapse guard is already
        // satisfied above, so it simply forwards to the GH base render).
        base.Render(canvas, graphics, channel);
        Bounds = outer;
    }

    /// <summary>
    /// Draws the bottom-centre grip handle. Override to suppress or restyle it.
    /// </summary>
    /// <param name="graphics">The GDI+ graphics context.</param>
    /// <param name="origin">The grip location (<see cref="BottomCentre"/>).</param>
    protected virtual void DrawGrip(Graphics graphics, PointF origin)
    {
        _grip.Location = origin;
        _grip.Draw(graphics);
    }

    /// <summary>
    /// Hook for subclasses to draw extra content (e.g. bezier wires) against the visual bounds,
    /// in the appropriate channel. The base implementation draws nothing.
    /// </summary>
    /// <param name="canvas">The Grasshopper canvas being rendered.</param>
    /// <param name="graphics">The GDI+ graphics context.</param>
    /// <param name="channel">The current rendering channel.</param>
    protected virtual void RenderGripContent(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
    }
}
