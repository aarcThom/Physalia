// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the CREST component that render a drag-to-link bezier wire
/// from CREST's bottom-centre grip to the linked ZOOID component.
/// </summary>
public class CrestAttrib : GH_ComponentAttributes
{
    private readonly Crest _crest; // the crest component

    private bool _isConnecting; // is the user adding a new zooid? False is disconnection
    private bool _isDragging; // is the user dragging the wire?
    private PointF _dragPoint; // the current positon of the drag

    private RectangleF _gripBounds; // the actual bounds of the grip
    private RectangleF _visualBounds; // the default bounds we want to render

    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAttrib"/> class.
    /// </summary>
    /// <param name="crest">The CREST component that owns these attributes.</param>
    public CrestAttrib(Crest crest)
        : base(crest)
    {
        _crest = crest;
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
    /// Renders the component and, when a ZOOID is linked or a drag is in progress,
    /// draws the bezier wire from the bottom grip to the ZOOID component.
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
            var whiteCircleBounds = new RectangleF(gripCtrX - gripRadius, gripCtrY - 4f, gripRadius * 2, gripRadius * 2);
            using var fill = new SolidBrush(Color.White);
            using var border = new Pen(Color.Black, 2f);
            graphics.FillEllipse(fill, whiteCircleBounds);
            graphics.DrawEllipse(border, whiteCircleBounds);
        }

        if (channel == GH_CanvasChannel.Wires)
        {
            if (_crest.ZooidComponent == null && !_isDragging)
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
                var zooidBounds = _crest.ZooidComponent.Attributes.Bounds;
                wireEnd = new PointF(zooidBounds.Left + zooidBounds.Width / 2f, zooidBounds.Y + zooidBounds.Height);
            }

            var crestBottomPoint = new PointF(gripCtrX, gripCtrY);

            // create the color gradient
            var phyBlue = Color.Blue;
            var phyPurple = Color.Purple;

            // draw a bezier curve between the CREST and ZOOID with a gradient that follows the curve
            var bezPt1 = new PointF(crestBottomPoint.X, crestBottomPoint.Y + 80f);
            var bezPt2 = new PointF(wireEnd.X, wireEnd.Y + 80f);

            // break the bezier into segments for a nice smooth gradient
            int steps = 40;
            for (int i = 0; i < steps; i++)
            {
                float t0 = (float)i / steps;
                float t1 = (float)(i + 1) / steps;

                var segStart = SampleBezier(crestBottomPoint, bezPt1, bezPt2, wireEnd, t0);
                var segEnd = SampleBezier(crestBottomPoint, bezPt1, bezPt2, wireEnd, t1);

                using var segPen = new Pen(LerpColor(phyBlue, phyPurple, t0), 2f);
                graphics.DrawLine(segPen, segStart, segEnd);
            }

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
        if (_gripBounds.Contains(e.CanvasLocation) && e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _dragPoint = e.CanvasLocation;

            // check if connecting or disconnecting
            if ((Control.ModifierKeys & Keys.Control) != 0) // is the user holding down ctrl
            {
                _isConnecting = false;
                sender.Cursor = Grasshopper.Instances.CursorServer.Cursor("GH_RemoveWire");
            }
            else
            {
                _isConnecting = true;
                sender.Cursor = Grasshopper.Instances.CursorServer.Cursor("GH_AddWire");
            }

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
    /// Completes the drag and links the CREST to a ZOOID component if the mouse was released over one.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if a drag was in progress; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_isDragging)
        {
            _isDragging = false;

            // can disattach if dragging to empty space
            if ( sender.Document.FindObject(e.CanvasLocation, 3f) == null)
            {
                _crest.ZooidComponent = null;
                _crest.ZooidGuid = Guid.Empty;

                sender.ScheduleRegen(2);
                return GH_ObjectResponse.Handled;
            }

            // cycle through the components on the canvas
            foreach (var obj in sender.Document.Objects)
            {
                if (obj is not Zooid && obj.Attributes.Bounds.Contains(e.CanvasLocation)) // filter out non-zooids
                {
                    _crest.ZooidComponent = null;
                    _crest.ZooidGuid = Guid.Empty;
                    break;
                }

                if (obj is Zooid zooid && zooid.Attributes.Bounds.Contains(e.CanvasLocation) && _isConnecting) // connect to a zooid
                {
                    _crest.ZooidComponent = zooid; // set the ref'd zooid
                    _crest.ZooidGuid = zooid.InstanceGuid;

                    break;
                }

                if (_crest.ZooidComponent != null && _crest.ZooidComponent.Attributes.Bounds.Contains(e.CanvasLocation) && !_isConnecting) // disconnecting a zooid with ctrl
                {
                    _crest.ZooidComponent = null;
                    _crest.ZooidGuid = Guid.Empty;

                    break;
                }
            }

            sender.ScheduleRegen(2);
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseUp(sender, e);
    }

    // HELPERS =======================================================================================

    // used to break the bezier into small chunks which can be assigned linear gradients
    private static PointF SampleBezier(PointF p0, PointF p1, PointF p2, PointF p3, float t)
    {
        float u = 1 - t;
        float x = (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X);
        float y = (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y);
        return new PointF(x, y);
    }

    // grab the color at paramter t along gradient between 2 colors
    private static Color LerpColor(Color a, Color b, float t)
        => Color.FromArgb(
            (int)(a.A + ((b.A - a.A) * t)),
            (int)(a.R + ((b.R - a.R) * t)),
            (int)(a.G + ((b.G - a.G) * t)),
            (int)(a.B + ((b.B - a.B) * t)));
}
