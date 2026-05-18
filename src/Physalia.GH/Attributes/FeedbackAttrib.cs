// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the Feedback component that render a drag-to-link bezier wire
/// from Feedback's bottom-centre grip to each linked FeedbackCollector.
/// Supports multiple simultaneous connections. Ctrl+drag disconnects.
/// </summary>
public class FeedbackAttrib : GH_ComponentAttributes
{
    private static readonly Pen[] _gradientPens = CreateGradientPens();

    private readonly Feedback _feedback;

    private bool _isConnecting;
    private bool _isDragging;
    private PointF _dragPoint;

    private RectangleF _gripBounds;
    private RectangleF _visualBounds;

    // bezier segment cache keyed by collector GUID — recomputed only when endpoints change
    private readonly Dictionary<Guid, (PointF[] Segments, PointF From, PointF To)> _segmentCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FeedbackAttrib"/> class.
    /// </summary>
    /// <param name="feedback">The Feedback component that owns these attributes.</param>
    public FeedbackAttrib(Feedback feedback)
        : base(feedback)
    {
        _feedback = feedback;
    }

    /// <summary>
    /// Expands the component bounds downward to include the bottom drag grip.
    /// </summary>
    protected override void Layout()
    {
        base.Layout();
        _visualBounds = Bounds;
        _gripBounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 10f);
        Bounds = _gripBounds;
    }

    /// <summary>
    /// Renders the component, the bottom grip, and bezier wires to all linked FeedbackCollectors.
    /// </summary>
    /// <param name="canvas">The Grasshopper canvas being rendered.</param>
    /// <param name="graphics">The GDI+ graphics context.</param>
    /// <param name="channel">The current rendering channel.</param>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        Bounds = _visualBounds;

        float gripCtrX = Bounds.Left + Bounds.Width / 2f;
        float gripCtrY = Bounds.Y + Bounds.Height;

        if (channel == GH_CanvasChannel.Objects)
        {
            var gripRadius = 4f;
            var circleBounds = new RectangleF(gripCtrX - gripRadius, gripCtrY - 4f, gripRadius * 2, gripRadius * 2);
            using var fill = new SolidBrush(Color.White);
            using var border = new Pen(Color.Black, 2f);
            graphics.FillEllipse(fill, circleBounds);
            graphics.DrawEllipse(border, circleBounds);
        }

        if (channel == GH_CanvasChannel.Wires)
        {
            var from = new PointF(gripCtrX, gripCtrY);

            foreach (var guid in _feedback.CollectorGuids)
            {
                var collector = canvas.Document?.FindObject(guid, false) as FeedbackCollector;
                if (collector == null) continue;

                var cb = collector.Attributes.Bounds;
                var to = new PointF(cb.Left + cb.Width / 2f, cb.Y + cb.Height);

                DrawBezier(graphics, from, to, guid);
                DrawArrowTip(graphics, to);
            }

            if (_isDragging)
            {
                DrawBezier(graphics, from, _dragPoint, Guid.Empty);
                DrawArrowTip(graphics, _dragPoint);
            }
        }

        base.Render(canvas, graphics, channel);
        Bounds = _gripBounds;
    }

    // EVENT HANDLERS ===================================================================================

    /// <summary>
    /// Begins a wire drag when the user presses the mouse button inside the bottom grip area.
    /// Hold Ctrl to enter disconnect mode.
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
            _isConnecting = (Control.ModifierKeys & Keys.Control) == 0;

            sender.Cursor = _isConnecting
                ? Grasshopper.Instances.CursorServer.Cursor("GH_AddWire")
                : Grasshopper.Instances.CursorServer.Cursor("GH_RemoveWire");

            sender.ScheduleRegen(2);
            return GH_ObjectResponse.Capture;
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
    /// Completes the drag. Connects to a FeedbackCollector on normal drop;
    /// disconnects from one on Ctrl+drop.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if a drag was in progress; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_isDragging)
        {
            _isDragging = false;

            foreach (var obj in sender.Document.Objects)
            {
                if (obj is FeedbackCollector collector && collector.Attributes.Bounds.Contains(e.CanvasLocation))
                {
                    if (_isConnecting)
                        _feedback.AddCollector(collector.InstanceGuid);
                    else
                        _feedback.RemoveCollector(collector.InstanceGuid);

                    _feedback.ExpireSolution(true);
                    break;
                }
            }

            sender.ScheduleRegen(2);
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseUp(sender, e);
    }

    // HELPERS ==========================================================================================

    private void DrawBezier(Graphics graphics, PointF from, PointF to, Guid cacheKey)
    {
        var cp1 = new PointF(from.X, from.Y + 80f);
        var cp2 = new PointF(to.X, to.Y + 80f);

        PointF[] segments;

        if (cacheKey != Guid.Empty
            && _segmentCache.TryGetValue(cacheKey, out var cached)
            && cached.From == from && cached.To == to)
        {
            segments = cached.Segments;
        }
        else
        {
            int steps = _gradientPens.Length;
            segments = new PointF[steps + 1];
            for (int i = 0; i <= steps; i++)
                segments[i] = SampleBezier(from, cp1, cp2, to, (float)i / steps);

            if (cacheKey != Guid.Empty)
                _segmentCache[cacheKey] = (segments, from, to);
        }

        for (int i = 0; i < _gradientPens.Length; i++)
            graphics.DrawLine(_gradientPens[i], segments[i], segments[i + 1]);
    }

    private static void DrawArrowTip(Graphics graphics, PointF wireEnd)
    {
        float triHeight = 8f;
        float triWidth = triHeight / 2f;
        var tip = new PointF(wireEnd.X, wireEnd.Y - triHeight);
        var baseLeft = new PointF(tip.X - triWidth / 2f, tip.Y + triHeight);
        var baseRight = new PointF(tip.X + triWidth / 2f, tip.Y + triHeight);
        using var triFill = new SolidBrush(Color.Purple);
        graphics.FillPolygon(triFill, new[] { tip, baseLeft, baseRight });
    }

    private static Pen[] CreateGradientPens()
    {
        const int steps = 40;
        var pens = new Pen[steps];
        for (int i = 0; i < steps; i++)
            pens[i] = new Pen(LerpColor(Color.Blue, Color.Purple, i / (float)steps), 2f);
        return pens;
    }

    private static PointF SampleBezier(PointF p0, PointF p1, PointF p2, PointF p3, float t)
    {
        float u = 1 - t;
        float x = (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X);
        float y = (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y);
        return new PointF(x, y);
    }

    private static Color LerpColor(Color a, Color b, float t)
        => Color.FromArgb(
            (int)(a.A + ((b.A - a.A) * t)),
            (int)(a.R + ((b.R - a.R) * t)),
            (int)(a.G + ((b.G - a.G) * t)),
            (int)(a.B + ((b.B - a.B) * t)));
}
