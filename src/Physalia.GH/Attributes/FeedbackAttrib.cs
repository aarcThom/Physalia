// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the Feedback component that render a drag-to-link bezier wire
/// from Feedback's bottom-centre grip to each linked FeedbackCollector.
/// Supports multiple simultaneous connections. Ctrl+drag disconnects.
/// </summary>
public class FeedbackAttrib : GH_ComponentAttributes
{
    private static readonly WireGradient _defaultGradient = new WireGradient(Color.Blue, Color.Purple);

    private readonly Feedback _feedback;

    private bool _isConnecting;
    private bool _isDragging;
    private PointF _dragPoint;

    private RectangleF _gripBounds;
    private RectangleF _visualBounds;

    private readonly Dictionary<Guid, BezierWire> _wireCache = new();
    private BezierWire? _dragWire;
    private readonly CanvasGrip _grip = new(PointF.Empty);

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
            _grip.Location = new PointF(gripCtrX, gripCtrY);
            _grip.Draw(graphics);
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

                if (!_wireCache.TryGetValue(guid, out var wire))
                {
                    wire = new BezierWire(from, to, _defaultGradient);
                    _wireCache[guid] = wire;
                }
                else
                {
                    wire.Start = from;
                    wire.End = to;
                }

                wire.Draw(graphics);
            }

            if (_isDragging)
            {
                if (_dragWire is null)
                    _dragWire = new BezierWire(from, _dragPoint, _defaultGradient);
                else
                {
                    _dragWire.Start = from;
                    _dragWire.End = _dragPoint;
                }

                _dragWire.Draw(graphics);
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
            _dragWire = null;

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
}
