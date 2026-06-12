// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;

namespace Physalia.GH.Attributes.UiElements;

/// <summary>
/// A small circular grip drawn on the canvas at a given location.
/// Used as the drag/drop handle on Feedback and FeedbackCollector components.
/// </summary>
public class CanvasGrip
{
    private static readonly float _radius = 4f;
    private static readonly float _borderWidth = 2f;

    /// <summary>
    /// Gets or sets the centre point of the grip in canvas coordinates.
    /// </summary>
    public PointF Location { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CanvasGrip"/> class.
    /// </summary>
    /// <param name="location">Centre of the grip in canvas coordinates.</param>
    public CanvasGrip(PointF location)
    {
        Location = location;
    }

    /// <summary>
    /// Draws the grip circle at <see cref="Location"/>.
    /// </summary>
    /// <param name="graphics">The GDI+ graphics context.</param>
    public void Draw(Graphics graphics)
    {
        var bounds = new RectangleF(
            Location.X - _radius,
            Location.Y - _radius,
            _radius * 2,
            _radius * 2);

        using var fill = new SolidBrush(Color.White);
        using var border = new Pen(Color.Black, _borderWidth);
        graphics.FillEllipse(fill, bounds);
        graphics.DrawEllipse(border, bounds);
    }
}
