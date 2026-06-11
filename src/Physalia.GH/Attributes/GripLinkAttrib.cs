// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Attributes.UiElements;

namespace Physalia.GH.Attributes;

/// <summary>
/// Base attributes for components that link to other document objects via a
/// bottom-centre drag grip and render bezier wires to each linked target.
/// Owns the grip layout, the drag state machine (drag to link, Ctrl+drag to
/// unlink), and wire rendering with a per-target cache. Subclasses define what
/// counts as a valid target, how links are stored, the wire colours, and where
/// on the target the wire lands.
/// </summary>
public abstract class GripLinkAttrib : GH_ComponentAttributes
{
    private readonly Dictionary<Guid, BezierWire> _wireCache = new();
    private readonly CanvasGrip _grip = new(PointF.Empty);

    private bool _isConnecting;
    private bool _isDragging;
    private PointF _dragPoint;

    private RectangleF _gripBounds;
    private RectangleF _visualBounds;

    private BezierWire? _dragWire;

    /// <summary>
    /// Initializes a new instance of the <see cref="GripLinkAttrib"/> class.
    /// </summary>
    /// <param name="component">The component that owns these attributes.</param>
    protected GripLinkAttrib(IGH_Component component)
        : base(component)
    {
    }

    /// <summary>
    /// Gets the gradient used for the link and drag wires.
    /// </summary>
    protected abstract WireGradient Gradient { get; }

    /// <summary>
    /// Gets the InstanceGuids of all currently linked targets.
    /// </summary>
    protected abstract IEnumerable<Guid> LinkedTargets { get; }

    /// <summary>
    /// Determines whether a document object is a valid drop target for this link type.
    /// </summary>
    /// <param name="obj">The document object under the drop point.</param>
    /// <returns>true if a drop on this object should connect or disconnect.</returns>
    protected abstract bool IsValidTarget(IGH_DocumentObject obj);

    /// <summary>
    /// Records a link to the target. Called on a normal drop over a valid target.
    /// </summary>
    /// <param name="targetGuid">The InstanceGuid of the drop target.</param>
    protected abstract void OnConnect(Guid targetGuid);

    /// <summary>
    /// Removes a link to the target. Called on a Ctrl+drop over a valid target.
    /// </summary>
    /// <param name="targetGuid">The InstanceGuid of the drop target.</param>
    protected abstract void OnDisconnect(Guid targetGuid);

    /// <summary>
    /// Returns the point on the target's bounds where the wire should land
    /// (e.g. bottom centre for incoming-feedback targets, top centre for script targets).
    /// </summary>
    /// <param name="targetBounds">The target's attribute bounds.</param>
    /// <returns>The wire end point in canvas coordinates.</returns>
    protected abstract PointF GetTargetAnchor(RectangleF targetBounds);

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
    /// Renders the component, the bottom grip, and bezier wires to all linked targets.
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

            foreach (var guid in LinkedTargets)
            {
                var target = canvas.Document?.FindObject(guid, false);
                if (target == null) continue;

                var to = GetTargetAnchor(target.Attributes.Bounds);

                if (!_wireCache.TryGetValue(guid, out var wire))
                {
                    wire = new BezierWire(from, to, Gradient);
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
                    _dragWire = new BezierWire(from, _dragPoint, Gradient);
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
    /// Updates the drag wire end point as the user moves the mouse.
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
    /// Completes the drag. Connects to a valid target on normal drop;
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
                if (obj.Attributes.Bounds.Contains(e.CanvasLocation) && IsValidTarget(obj))
                {
                    // Endpoints may change after a link edit — rebuild wires on the next frame.
                    _wireCache.Clear();

                    if (_isConnecting)
                        OnConnect(obj.InstanceGuid);
                    else
                        OnDisconnect(obj.InstanceGuid);

                    Owner.ExpireSolution(true);
                    break;
                }
            }

            sender.ScheduleRegen(2);
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseUp(sender, e);
    }
}
