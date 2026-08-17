// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Drawing;

namespace Physalia.GH.Attributes.UiElements;

/// <summary>
/// Defines the colour gradient applied along a <see cref="BezierWire"/>.
/// </summary>
/// <param name="From">Colour at the start of the wire.</param>
/// <param name="To">Colour at the end of the wire.</param>
/// <param name="Steps">Number of line segments used to approximate the curve.</param>
public record WireGradient(Color From, Color To, int Steps = 40);

/// <summary>
/// A standalone cubic bezier wire element.
/// Caches its sampled segments and only recomputes when endpoints change.
/// Draws the wire and an arrow tip at the end point.
/// </summary>
public class BezierWire
{
    private static readonly float _controlOffset = 80f;

    // The least a turning wire may push past its own endpoint. Only floors the elbow: it keeps a
    // wire that leaves rightwards and arrives from below from having to turn in no room at all when
    // its two ends are nearly level.
    private static readonly float _elbowMinimum = 30f;

    private PointF _start;
    private PointF _end;
    private WireGradient _gradient;
    private bool _horizontalStart;
    private bool _horizontalEnd;
    private IArrowHead _arrowHead = TriangleArrowHead.Default;

    private Pen[] _pens;
    private PointF[] _segments = Array.Empty<PointF>();
    private bool _dirty = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="BezierWire"/> class.
    /// </summary>
    /// <param name="start">Canvas position of the wire origin.</param>
    /// <param name="end">Canvas position of the wire target.</param>
    /// <param name="gradient">Colour gradient to paint along the wire.</param>
    public BezierWire(PointF start, PointF end, WireGradient gradient)
    {
        _start = start;
        _end = end;
        _gradient = gradient;
        _pens = BuildPens(gradient);
    }

    /// <summary>Gets or sets the wire origin. Setting a new value invalidates the segment cache.</summary>
    public PointF Start
    {
        get => _start;
        set
        {
            if (_start == value) return;
            _start = value;
            _dirty = true;
        }
    }

    /// <summary>Gets or sets the wire target. Setting a new value invalidates the segment cache.</summary>
    public PointF End
    {
        get => _end;
        set
        {
            if (_end == value) return;
            _end = value;
            _dirty = true;
        }
    }

    /// <summary>Gets or sets the colour gradient. Setting a new value rebuilds the pen array.</summary>
    public WireGradient Gradient
    {
        get => _gradient;
        set
        {
            _gradient = value;
            _pens = BuildPens(value);
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the wire approaches its end horizontally (from the
    /// left) with a rightward arrow tip, rather than the default vertical approach with an upward
    /// tip. Setting a new value invalidates the segment cache.
    /// </summary>
    public bool HorizontalEnd
    {
        get => _horizontalEnd;
        set
        {
            if (_horizontalEnd == value) return;
            _horizontalEnd = value;
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the wire LEAVES its start heading right, rather than
    /// the default downward departure. Setting a new value invalidates the segment cache.
    ///
    /// <para>Must match the edge the grip sits on, or the wire sets off across the node it came from:
    /// a bottom-centre grip departs downward, a right-edge grip departs rightwards.</para>
    /// </summary>
    public bool HorizontalStart
    {
        get => _horizontalStart;
        set
        {
            if (_horizontalStart == value) return;
            _horizontalStart = value;
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the head drawn at <see cref="End"/>. Defaults to a filled triangle; assign a
    /// different <see cref="IArrowHead"/> to change the tip ornament without touching the wire.
    /// </summary>
    public IArrowHead ArrowHead
    {
        get => _arrowHead;
        set => _arrowHead = value ?? TriangleArrowHead.Default;
    }

    /// <summary>
    /// Draws the bezier wire and an arrow tip at <see cref="End"/>.
    /// </summary>
    /// <param name="graphics">The GDI+ graphics context.</param>
    public void Draw(Graphics graphics)
    {
        if (_dirty) Recompute();

        for (int i = 0; i < _pens.Length; i++)
            graphics.DrawLine(_pens[i], _segments[i], _segments[i + 1]);

        _arrowHead.Draw(graphics, _end, EndDirection(), _gradient.To);
    }

    // -------------------------------------------------------------------------

    // Unit travel direction of the wire at its end, taken from the last sampled segment. Falls back
    // to the curve's nominal approach (rightward when horizontal, upward otherwise) when degenerate.
    private PointF EndDirection()
    {
        int n = _segments.Length;
        if (n >= 2)
        {
            float dx = _segments[n - 1].X - _segments[n - 2].X;
            float dy = _segments[n - 1].Y - _segments[n - 2].Y;
            float len = (float)Math.Sqrt((dx * dx) + (dy * dy));
            if (len > 0.0001f)
            {
                return new PointF(dx / len, dy / len);
            }
        }

        return _horizontalEnd ? new PointF(1f, 0f) : new PointF(0f, -1f);
    }

    /// <summary>
    /// The two control points, which decide the whole shape of the wire. Each one pushes the curve
    /// out perpendicular to the edge its endpoint sits on, so the wire leaves and arrives square to
    /// the node rather than cutting across it.
    ///
    /// <para>When the two ends AGREE — both horizontal or both vertical — a fixed push either side
    /// gives the usual slack S-curve. When they disagree, the wire has to TURN, and a fixed push is
    /// wrong: pushing 80 below an endpoint that is only 40 above the grip drags the wire down before
    /// it climbs, which reads as a sag rather than a turn. So both control points go to the ELBOW
    /// between the ends instead — the corner that is level with the start and plumb with the end.
    /// The wire then runs flat out of the grip, turns once, and rises into its target, and the shape
    /// scales with the gap on its own: a distant target gets a long flat run and a late turn, a near
    /// one a rounder corner.</para>
    ///
    /// <para>The elbow is clamped away from the start on both axes so the turn always has room to
    /// happen: a target to the LEFT still leaves rightwards (the grip is on a right edge, and a wire
    /// setting off left would cross its own node), and a target BELOW still arrives from underneath,
    /// which is what keeps the tip pointing up at it.</para>
    /// </summary>
    /// <returns>The first and second cubic control points.</returns>
    private (PointF Cp1, PointF Cp2) ControlPoints()
    {
        if (_horizontalStart && !_horizontalEnd)
        {
            return (
                new PointF(Math.Max(_end.X, _start.X + _elbowMinimum), _start.Y),
                new PointF(_end.X, Math.Max(_start.Y, _end.Y + _elbowMinimum)));
        }

        if (!_horizontalStart && _horizontalEnd)
        {
            return (
                new PointF(_start.X, Math.Max(_end.Y, _start.Y + _elbowMinimum)),
                new PointF(Math.Min(_start.X, _end.X - _elbowMinimum), _end.Y));
        }

        return (
            _horizontalStart
                ? new PointF(_start.X + _controlOffset, _start.Y)
                : new PointF(_start.X, _start.Y + _controlOffset),
            _horizontalEnd
                ? new PointF(_end.X - _controlOffset, _end.Y)
                : new PointF(_end.X, _end.Y + _controlOffset));
    }

    private void Recompute()
    {
        (PointF cp1, PointF cp2) = ControlPoints();

        int steps = _gradient.Steps;
        _segments = new PointF[steps + 1];
        for (int i = 0; i <= steps; i++)
            _segments[i] = SampleBezier(_start, cp1, cp2, _end, (float)i / steps);

        _dirty = false;
    }

    private static Pen[] BuildPens(WireGradient gradient)
    {
        var pens = new Pen[gradient.Steps];
        for (int i = 0; i < gradient.Steps; i++)
            pens[i] = new Pen(Lerp(gradient.From, gradient.To, i / (float)gradient.Steps), 2f);
        return pens;
    }

    private static PointF SampleBezier(PointF p0, PointF p1, PointF p2, PointF p3, float t)
    {
        float u = 1 - t;
        return new PointF(
            (u * u * u * p0.X) + (3 * u * u * t * p1.X) + (3 * u * t * t * p2.X) + (t * t * t * p3.X),
            (u * u * u * p0.Y) + (3 * u * u * t * p1.Y) + (3 * u * t * t * p2.Y) + (t * t * t * p3.Y));
    }

    private static Color Lerp(Color a, Color b, float t)
        => Color.FromArgb(
            (int)(a.A + ((b.A - a.A) * t)),
            (int)(a.R + ((b.R - a.R) * t)),
            (int)(a.G + ((b.G - a.G) * t)),
            (int)(a.B + ((b.B - a.B) * t)));
}
