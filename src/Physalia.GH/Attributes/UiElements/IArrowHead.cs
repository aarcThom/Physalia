// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;

namespace Physalia.GH.Attributes.UiElements;

/// <summary>
/// Draws the tip ornament at the end of a <see cref="BezierWire"/>. Implementations are pluggable
/// so a wire can terminate in a filled triangle, a chevron, a dot, or nothing at all, oriented to
/// the wire's incoming direction.
/// </summary>
public interface IArrowHead
{
    /// <summary>
    /// Draws the head at the wire's end point, pointing along the wire's travel direction.
    /// </summary>
    /// <param name="graphics">The GDI+ graphics context.</param>
    /// <param name="end">The wire end point, in canvas coordinates — where the head sits.</param>
    /// <param name="direction">The unit travel direction of the wire at its end (where the tip points).</param>
    /// <param name="color">The colour to paint the head.</param>
    void Draw(Graphics graphics, PointF end, PointF direction, Color color);
}
