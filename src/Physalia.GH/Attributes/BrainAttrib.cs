// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the BRAIN component that render a drag-to-link bezier wire
/// from BRAIN's bottom-centre grip to the linked BODY component.
/// </summary>
public class BrainAttrib : GH_ComponentAttributes
{
    private readonly Brain _brain; // the brain component

    private bool _isDragging; // is the user dragging the wire?
    private PointF _dragPoint; // the current positon of the drag

    private RectangleF _gripBounds; // the actual bounds of the grip
    private RectangleF _visualBounds; // the default bounds we want to render

    /// <summary>
    /// Initializes a new instance of the <see cref="BrainAttrib"/> class.
    /// </summary>
    /// <param name="brain">The BRAIN component that owns these attributes.</param>
    public BrainAttrib(Brain brain) : base(brain)
    {
        _brain = brain;
    }

    /// <summary>
    /// Lays out the component bounds, expanding them downward to include the bottom drag grip.
    /// </summary>
    protected override void Layout()
    {
        base.Layout();
        _visualBounds = Bounds; // store the original layout

        _gripBounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 10f); // get the expanded clickable bounds

        Bounds = _gripBounds; // set the layout bounds to the expanded bounds
    }

    /// <summary>
    /// Renders the component and, when a BODY is linked or a drag is in progress,
    /// draws the bezier wire from the bottom grip to the BODY component.
    /// </summary>
    /// <param name="canvas">The Grasshopper canvas being rendered.</param>
    /// <param name="graphics">The GDI+ graphics context.</param>
    /// <param name="channel">The current rendering channel.</param>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        Bounds = _visualBounds; // SET THE BOUNDS BACK TO DEFAULT FOR RENDER PASS

        /* Render is called multiple times per canvas redraw — once for each rendering channel
        * (e.g.Wires, Objects, Overlay, etc.)
        * Only draw the custom bezier when GH wires are being drawn
        */

        // the grip is at the bottom of the component like galapagos
        float gripCtrX = Bounds.Left + Bounds.Width / 2f;
        float gripCtrY = Bounds.Y + Bounds.Height;

        // if drawing the objects draw the little white circle for the bottom grip
        if (channel == GH_CanvasChannel.Objects)
        {
            var gripRadius = 4f;
            var whiteCircleBounds = new RectangleF(gripCtrX - gripRadius, gripCtrY - 2f, gripRadius * 2, gripRadius * 2);
            using var fill = new SolidBrush(Color.White);
            using var border = new Pen(Color.Black, 2f);
            graphics.FillEllipse(fill, whiteCircleBounds);
            graphics.DrawEllipse(border, whiteCircleBounds);
        }

        if (channel == GH_CanvasChannel.Wires)
        {
            if (_brain.BodyComponent == null && !_isDragging)
            {
                base.Render(canvas, graphics, channel);
                Bounds = _gripBounds; // revert back to expanded bounds for clickable grip
                return;
            }

            PointF wireEnd;
            if (_isDragging)
            {
                wireEnd = _dragPoint;
            }
            else
            {
                var bodyBounds = _brain.BodyComponent.Attributes.Bounds;
                wireEnd = new PointF(bodyBounds.Left + bodyBounds.Width / 2f, bodyBounds.Y + bodyBounds.Height);
            }

            var brainBottomPt = new PointF(gripCtrX, gripCtrY);

            float brainBodyMidPt = (wireEnd.X - brainBottomPt.X) * 0.5f;

            // create the color gradient
            var phyBlue = Color.Blue;
            var phyPurple = Color.Purple;

            // need to extend the gradient end points PAST the bezier curve otherwise weird clipping occurs.
            // replace the below with something more elegant and not hardcoded
            var gradStart = new PointF(brainBottomPt.X - 100f, brainBottomPt.Y - 100f);
            var gradEnd = new PointF(wireEnd.X + 100f, wireEnd.Y + 100f);

            using var gradient = new LinearGradientBrush(gradStart, wireEnd, phyBlue, phyPurple);
            using var pen = new Pen(gradient, 2f);

            // draw a bezier curver between the BRAIN and BODY
            var bezPt1 = new PointF(brainBottomPt.X, brainBottomPt.Y + 80f);
            var bezPt2 = new PointF(wireEnd.X, wireEnd.Y + 80f);
            graphics.DrawBezier(pen, brainBottomPt, bezPt1, bezPt2, wireEnd);

            //draw the triangle at the tip
            float triHeight = 8f;
            float triWidth = triHeight / 2f;

            var tip = new PointF(wireEnd.X, wireEnd.Y - triHeight);
            var baseLeft = new PointF(tip.X - triWidth / 2f, tip.Y + triHeight);
            var baseRight = new PointF(tip.X + triWidth / 2f, tip.Y + triHeight);
            using var triFill = new SolidBrush(phyPurple);
            graphics.FillPolygon(triFill, new[] { tip, baseLeft, baseRight });
        }

        base.Render(canvas, graphics, channel);
        Bounds = _gripBounds; // reset the bounds to the expanded area after the render pass
    }

    // EVENT HANDLERS =======================================================================================

    /// <summary>
    /// Begins a wire drag when the user presses the mouse button inside the bottom grip area.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Capture if the grip was hit; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_gripBounds.Contains(e.CanvasLocation))
        {
            _isDragging = true;
            _dragPoint = e.CanvasLocation;
            sender.ScheduleRegen(2);

            return GH_ObjectResponse.Capture; // object is active
        }
        return base.RespondToMouseDown(sender, e);
    }

    /// <summary>
    /// Updates the wire end position as the user drags across the canvas.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if a drag is in progress; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_isDragging)
        {
            _dragPoint = e.CanvasLocation;
            sender.ScheduleRegen(2);

            return GH_ObjectResponse.Handled;
        }
        return base.RespondToMouseMove(sender, e);
    }

    /// <summary>
    /// Completes the drag and links the BRAIN to a BODY component if the mouse was released over one.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if a drag was in progress; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_isDragging)
        {
            _isDragging = false;

            // cycle through the components on the canvas
            foreach (var obj in sender.Document.Objects)
            {
                if (obj is Body body && body.Attributes.Bounds.Contains(e.CanvasLocation))
                {
                    _brain.BodyComponent = body; // set the ref'd body
                    _brain.BodyGuid = body.InstanceGuid;

                    break;
                }
            }
            sender.ScheduleRegen(2);
            return GH_ObjectResponse.Handled;
        }
        return base.RespondToMouseUp(sender, e);
    }
}
