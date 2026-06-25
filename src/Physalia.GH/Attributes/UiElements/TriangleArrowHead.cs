// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;

namespace Physalia.GH.Attributes.UiElements;

/// <summary>
/// The default <see cref="IArrowHead"/>: a solid filled triangle. The wire end sits at the centre
/// of the triangle's base and the tip extends <see cref="Height"/> along the wire direction, with
/// the base spanning <see cref="Width"/> either side.
/// </summary>
public sealed class TriangleArrowHead : IArrowHead
{
    /// <summary>
    /// The shared default triangle head, matching the wire tip used throughout the canvas.
    /// </summary>
    public static readonly TriangleArrowHead Default = new();

    /// <summary>
    /// Gets or sets the distance from the base centre to the tip, along the wire direction.
    /// </summary>
    public float Height { get; set; } = 8f;

    /// <summary>
    /// Gets or sets the half-width of the triangle base, perpendicular to the wire direction.
    /// </summary>
    public float Width { get; set; } = 4f;

    /// <inheritdoc/>
    public void Draw(Graphics graphics, PointF end, PointF direction, Color color)
    {
        // Tip extends along the travel direction; the base spans the perpendicular at the wire end.
        var tip = new PointF(end.X + (Height * direction.X), end.Y + (Height * direction.Y));
        var perp = new PointF(-direction.Y, direction.X);
        var baseA = new PointF(end.X + (Width * perp.X), end.Y + (Width * perp.Y));
        var baseB = new PointF(end.X - (Width * perp.X), end.Y - (Width * perp.Y));

        using var fill = new SolidBrush(color);
        graphics.FillPolygon(fill, new[] { tip, baseA, baseB });
    }
}
