// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Components;
using Physalia.GH.Harness;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the Component Transmitter. Renders a bezier arrow from the
/// bottom-centre grip to a free canvas point — unlike <see cref="GripLinkAttrib"/>, the
/// drop lands anywhere (no target object), and its tip marks the top-left origin where the
/// transmitter places its GhJSON graph. The point is stored as an offset from the
/// component's pivot, so the arrow travels with the component as it is moved.
/// Drag the grip to set the point; clear it via the component's right-click menu.
/// </summary>
public class CompTxAttrib : GH_ComponentAttributes
{
    private static readonly WireGradient _gradient = new WireGradient(Color.Orange, Color.MediumOrchid);

    // Canvas-unit radius around the arrow tip within which a Ctrl+press grabs it to reposition.
    private const float TipHitRadius = 10f;

    private readonly CanvasGrip _grip = new(PointF.Empty);
    private readonly ComponentTransmitter _transmitter;

    private bool _isDragging;
    private PointF _dragPoint;

    private RectangleF _gripBounds;
    private RectangleF _visualBounds;

    private BezierWire? _wire;
    private BezierWire? _dragWire;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompTxAttrib"/> class.
    /// </summary>
    /// <param name="transmitter">The Component Transmitter that owns these attributes.</param>
    public CompTxAttrib(ComponentTransmitter transmitter)
        : base(transmitter)
    {
        _transmitter = transmitter;
    }

    /// <summary>
    /// Expands the component bounds downward to include the bottom drag grip.
    /// </summary>
    protected override void Layout()
    {
        if (CollapseGuard.TryCollapseLayout(this))
        {
            _visualBounds = Bounds;
            _gripBounds = Bounds;
            return;
        }

        base.Layout();
        _visualBounds = Bounds;
        _gripBounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 10f);
        Bounds = _gripBounds;
    }

    /// <summary>
    /// Renders the component, the bottom grip, the placement-target arrow, and the live drag wire.
    /// </summary>
    /// <param name="canvas">The Grasshopper canvas being rendered.</param>
    /// <param name="graphics">The GDI+ graphics context.</param>
    /// <param name="channel">The current rendering channel.</param>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (CollapseGuard.IsCollapsed(this))
        {
            return;
        }

        Bounds = _visualBounds;

        float gripCtrX = Bounds.Left + Bounds.Width / 2f;
        float gripCtrY = Bounds.Y + Bounds.Height;
        var from = new PointF(gripCtrX, gripCtrY);

        if (channel == GH_CanvasChannel.Objects)
        {
            _grip.Location = from;
            _grip.Draw(graphics);
        }

        if (channel == GH_CanvasChannel.Wires)
        {
            // Hide the settled arrow while a drag is in flight, so only the live drag wire shows.
            if (!_isDragging && _transmitter.PlacementTarget is { } target)
            {
                if (_wire is null)
                    _wire = new BezierWire(from, target, _gradient);
                else
                {
                    _wire.Start = from;
                    _wire.End = target;
                }

                _wire.Draw(graphics);
            }

            if (_isDragging)
            {
                if (_dragWire is null)
                    _dragWire = new BezierWire(from, _dragPoint, _gradient);
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
    /// Begins an arrow drag. A press inside the bottom grip starts a fresh arrow; a Ctrl+press
    /// on the existing arrow tip grabs it to reposition the placement target directly.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Capture if a drag began; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button == MouseButtons.Left)
        {
            bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            bool overTip = ctrl
                && _transmitter.PlacementTarget is { } tip
                && IsNear(e.CanvasLocation, tip, TipHitRadius);

            if (overTip || _gripBounds.Contains(e.CanvasLocation))
            {
                _isDragging = true;
                _dragPoint = e.CanvasLocation;
                sender.Cursor = Grasshopper.Instances.CursorServer.Cursor("GH_AddWire");
                sender.ScheduleRegen(2);
                return GH_ObjectResponse.Capture;
            }
        }

        return base.RespondToMouseDown(sender, e);
    }

    private static bool IsNear(PointF a, PointF b, float radius)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    /// <summary>
    /// Updates the drag arrow end point as the user moves the mouse.
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
    /// Completes the drag, storing the drop point as the transmitter's placement target.
    /// The drop lands anywhere on the canvas — there is no target object to validate.
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
            _wire = null; // endpoints change — rebuild on the next frame.

            _transmitter.SetPlacementTarget(e.CanvasLocation);
            _transmitter.ExpireSolution(true);

            sender.ScheduleRegen(2);
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseUp(sender, e);
    }
}
