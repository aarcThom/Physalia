// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Drawing;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace Physalia.GH.Attributes.UiElements;

/// <summary>
/// Supplies an <see cref="ArrowGrip"/> with everything it needs to draw and commit a drag arrow.
/// Implemented by the host attribute (Feedback/PyTransmitter/ZoomGuid via <c>GripLinkAttrib</c>,
/// the Component Transmitter, and the collapsed-harness Chatbox proxy), which keeps only the parts
/// that genuinely differ — where the arrow starts, what colour and head it uses, where its settled
/// ends land, and what a drop means.
/// </summary>
public interface IArrowHost
{
    /// <summary>Gets the canvas point the arrow starts from (the bottom-centre grip).</summary>
    PointF ArrowOrigin { get; }

    /// <summary>Gets the gradient painted along the arrow's wires.</summary>
    WireGradient ArrowGradient { get; }

    /// <summary>Gets the head drawn at each wire's end.</summary>
    IArrowHead ArrowHead { get; }

    /// <summary>
    /// Gets a value indicating whether the arrow approaches its end horizontally (rightward tip)
    /// rather than the default vertical approach (upward tip).
    /// </summary>
    bool HorizontalArrow { get; }

    /// <summary>
    /// Returns the canvas points where the settled (non-drag) wires currently land. Empty when
    /// nothing is connected yet.
    /// </summary>
    /// <param name="doc">The document to resolve targets against.</param>
    /// <returns>The settled wire end points, in canvas coordinates.</returns>
    IEnumerable<PointF> SettledEndpoints(GH_Document doc);

    /// <summary>
    /// Commits a completed drag: link/unlink a target, or store a placement point, then expire so
    /// the change takes effect.
    /// </summary>
    /// <param name="doc">The document the drop landed in.</param>
    /// <param name="dropPoint">The drop point in canvas coordinates.</param>
    /// <param name="ctrl">Whether the drag was started with the disconnect (Ctrl) intent.</param>
    void OnDrop(GH_Document doc, PointF dropPoint, bool ctrl);
}

/// <summary>
/// The reusable mechanics of a drag arrow: a per-frame-cached set of settled
/// <see cref="BezierWire"/>s, the live drag wire, and the drag state machine. The grip dot itself
/// is drawn by the host (see <c>BottomGripAttributes</c>); this controller owns only the wires and
/// the drag. Hosts forward their render/mouse calls to it, supplying their specifics through
/// <see cref="IArrowHost"/>. This is the single place the arrow draw + drag logic lives, shared by
/// the link attributes, the Component Transmitter, and the collapsed-harness proxy.
/// </summary>
public sealed class ArrowGrip
{
    private readonly List<BezierWire> _wires = new();

    private BezierWire? _dragWire;
    private bool _isDragging;
    private bool _dragCtrl;
    private PointF _dragPoint;

    /// <summary>Gets a value indicating whether a drag is currently in progress.</summary>
    public bool IsDragging => _isDragging;

    /// <summary>
    /// Draws the settled wires to the host's current endpoints and, while dragging, the live drag
    /// wire (the Wires channel). The settled wires are hidden mid-drag so only the drag wire shows.
    /// </summary>
    /// <param name="graphics">The GDI+ graphics context.</param>
    /// <param name="doc">The current document, or null when unavailable.</param>
    /// <param name="host">The host supplying origin, colours, and endpoints.</param>
    public void DrawWires(Graphics graphics, GH_Document? doc, IArrowHost host)
    {
        PointF from = host.ArrowOrigin;

        int count = 0;
        if (!_isDragging && doc is not null)
        {
            foreach (PointF to in host.SettledEndpoints(doc))
            {
                BezierWire wire = WireAt(count++, from, to, host);
                wire.Draw(graphics);
            }
        }

        while (_wires.Count > count)
        {
            _wires.RemoveAt(_wires.Count - 1);
        }

        if (_isDragging)
        {
            if (_dragWire is null)
            {
                _dragWire = NewWire(from, _dragPoint, host);
            }
            else
            {
                _dragWire.Start = from;
                _dragWire.End = _dragPoint;
            }

            _dragWire.Draw(graphics);
        }
    }

    /// <summary>
    /// Begins a drag from the grip. The cursor reflects the intent: a remove cursor when
    /// <paramref name="ctrl"/> (disconnect), otherwise an add cursor.
    /// </summary>
    /// <param name="canvas">The Grasshopper canvas.</param>
    /// <param name="point">The starting canvas point.</param>
    /// <param name="ctrl">The disconnect intent, captured at drag start and replayed on drop.</param>
    public void StartDrag(GH_Canvas canvas, PointF point, bool ctrl)
    {
        _isDragging = true;
        _dragCtrl = ctrl;
        _dragPoint = point;

        canvas.Cursor = Grasshopper.Instances.CursorServer.Cursor(ctrl ? "GH_RemoveWire" : "GH_AddWire");
        canvas.ScheduleRegen(2);
    }

    /// <summary>
    /// Updates the live drag wire end as the mouse moves.
    /// </summary>
    /// <param name="canvas">The Grasshopper canvas.</param>
    /// <param name="point">The current canvas point.</param>
    public void UpdateDrag(GH_Canvas canvas, PointF point)
    {
        _dragPoint = point;
        canvas.ScheduleRegen(2);
    }

    /// <summary>
    /// Ends the drag and forwards the drop to the host (with the intent captured at start).
    /// </summary>
    /// <param name="canvas">The Grasshopper canvas.</param>
    /// <param name="doc">The document the drop landed in, or null.</param>
    /// <param name="point">The drop canvas point.</param>
    /// <param name="host">The host that commits the drop.</param>
    public void EndDrag(GH_Canvas canvas, GH_Document? doc, PointF point, IArrowHost host)
    {
        _isDragging = false;
        _dragWire = null;

        if (doc is not null)
        {
            host.OnDrop(doc, point, _dragCtrl);
        }

        canvas.ScheduleRegen(2);
    }

    // Reuses a cached wire (preserving its sampled-segment cache across frames) or grows the list.
    // Colour/head/orientation are constant per host, so they are set only on creation.
    private BezierWire WireAt(int index, PointF from, PointF to, IArrowHost host)
    {
        if (index < _wires.Count)
        {
            BezierWire wire = _wires[index];
            wire.Start = from;
            wire.End = to;
            return wire;
        }

        BezierWire created = NewWire(from, to, host);
        _wires.Add(created);
        return created;
    }

    private static BezierWire NewWire(PointF from, PointF to, IArrowHost host) =>
        new(from, to, host.ArrowGradient)
        {
            HorizontalEnd = host.HorizontalArrow,
            ArrowHead = host.ArrowHead,
        };
}
