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
    private static readonly float _arrowHeight = 8f;
    private static readonly float _arrowWidth = 4f;

    private PointF _start;
    private PointF _end;
    private WireGradient _gradient;
    private bool _horizontalEnd;

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
    /// Draws the bezier wire and an arrow tip at <see cref="End"/>.
    /// </summary>
    /// <param name="graphics">The GDI+ graphics context.</param>
    public void Draw(Graphics graphics)
    {
        if (_dirty) Recompute();

        for (int i = 0; i < _pens.Length; i++)
            graphics.DrawLine(_pens[i], _segments[i], _segments[i + 1]);

        Color arrowColor = _gradient.To;

        DrawArrow(graphics, _end, arrowColor, _horizontalEnd);
    }

    // -------------------------------------------------------------------------

    private void Recompute()
    {
        var cp1 = new PointF(_start.X, _start.Y + _controlOffset);
        var cp2 = _horizontalEnd
            ? new PointF(_end.X - _controlOffset, _end.Y)
            : new PointF(_end.X, _end.Y + _controlOffset);

        int steps = _gradient.Steps;
        _segments = new PointF[steps + 1];
        for (int i = 0; i <= steps; i++)
            _segments[i] = SampleBezier(_start, cp1, cp2, _end, (float)i / steps);

        _dirty = false;
    }

    private static void DrawArrow(Graphics graphics, PointF wireEnd, Color arrowColor, bool horizontal)
    {
        PointF tip, baseA, baseB;
        if (horizontal)
        {
            // Points right; the wire end is the base centre, the tip extends to the right.
            tip = new PointF(wireEnd.X + _arrowHeight, wireEnd.Y);
            baseA = new PointF(wireEnd.X, wireEnd.Y - _arrowWidth);
            baseB = new PointF(wireEnd.X, wireEnd.Y + _arrowWidth);
        }
        else
        {
            tip = new PointF(wireEnd.X, wireEnd.Y - _arrowHeight);
            baseA = new PointF(tip.X - _arrowWidth, tip.Y + _arrowHeight);
            baseB = new PointF(tip.X + _arrowWidth, tip.Y + _arrowHeight);
        }

        using var fill = new SolidBrush(arrowColor);
        graphics.FillPolygon(fill, new[] { tip, baseA, baseB });
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
