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

    /// <summary>Gets the bottom-centre of the visible capsule.</summary>
    protected PointF BottomCentre =>
        new(_visualBounds.Left + (_visualBounds.Width / 2f), _visualBounds.Y + _visualBounds.Height);

    /// <summary>Gets the right-edge midpoint of the visible capsule, where a Grasshopper output leaves from.</summary>
    protected PointF RightCentre =>
        new(_visualBounds.Right, _visualBounds.Y + (_visualBounds.Height / 2f));

    /// <summary>
    /// Gets the point the grip is drawn at and a wire leaves from. Bottom-centre by default, which is
    /// what this class is named for; a subclass can put it elsewhere, and must widen the matching side
    /// of the pick region through <see cref="ExpandForGrip"/> to keep it hittable.
    /// </summary>
    protected virtual PointF GripOrigin => BottomCentre;

    /// <summary>
    /// Gets the region a press must land in to grab the grip — a square centred on
    /// <see cref="GripOrigin"/>, reaching <see cref="GripExpansion"/> each way.
    ///
    /// <para>Distinct from <see cref="GripBounds"/> on purpose. That is the PICK region, and it has to
    /// cover the whole node so Grasshopper routes a mouse-down here at all; this is the much smaller
    /// patch where that press means "pull a wire out" rather than "move me". Testing the pick region
    /// instead — which is what the drag used to do — makes every press anywhere on the node start a
    /// wire, so the component cannot be dragged at all.</para>
    /// </summary>
    protected RectangleF GripHitRegion
    {
        get
        {
            PointF origin = GripOrigin;
            return new RectangleF(
                origin.X - GripExpansion,
                origin.Y - GripExpansion,
                GripExpansion * 2f,
                GripExpansion * 2f);
        }
    }

    /// <inheritdoc/>
    protected override void Layout()
    {
        base.Layout();

        _visualBounds = AdjustVisualBounds(Bounds);
        _gripBounds = ExpandForGrip(_visualBounds);
        Bounds = _gripBounds;
    }

    /// <summary>
    /// Expands the capsule rect into the pick region that makes the grip hittable — downward by
    /// default, matching a bottom grip.
    /// </summary>
    /// <param name="visual">The capsule rect.</param>
    /// <returns>The pick region: the capsule plus a strip on the grip's side.</returns>
    protected virtual RectangleF ExpandForGrip(RectangleF visual) =>
        new(visual.X, visual.Y, visual.Width, visual.Height + GripExpansion);

    /// <summary>
    /// Hook for a subclass to resize or reposition the capsule after Grasshopper has laid it out from
    /// its parameters, but before the grip strip is measured against it.
    ///
    /// <para>The seam exists because the grip, the wire origin
    /// (<see cref="BottomCentre"/>) and the pick region all derive from the capsule rect: adjusting
    /// <see cref="Bounds"/> after the fact would leave those three disagreeing with what is drawn.
    /// The base implementation changes nothing.</para>
    /// </summary>
    /// <param name="bounds">The capsule rect Grasshopper computed.</param>
    /// <returns>The rect the capsule should actually occupy.</returns>
    protected virtual RectangleF AdjustVisualBounds(RectangleF bounds) => bounds;

    /// <inheritdoc/>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        // The component and grip draw against the un-expanded bounds; restore the pick region after.
        RectangleF outer = Bounds;
        Bounds = _visualBounds;

        if (channel == GH_CanvasChannel.Objects)
        {
            DrawGrip(graphics, GripOrigin);
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
