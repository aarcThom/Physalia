// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the FeedbackCollector component that render a bottom-centre grip
/// as the visual target for bezier wires arriving from paired Feedback components.
/// </summary>
public class FeedbackCollectorAttrib : GH_ComponentAttributes
{
    private RectangleF _gripBounds;
    private RectangleF _visualBounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="FeedbackCollectorAttrib"/> class.
    /// </summary>
    /// <param name="feedbackCollector">The FeedbackCollector component that owns these attributes.</param>
    public FeedbackCollectorAttrib(FeedbackCollector feedbackCollector)
        : base(feedbackCollector)
    {
    }

    /// <summary>
    /// Expands the component bounds downward to include the bottom grip target area.
    /// </summary>
    protected override void Layout()
    {
        base.Layout();
        _visualBounds = Bounds;
        _gripBounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 10f);
        Bounds = _gripBounds;
    }

    /// <summary>
    /// Renders the component and a white circle grip at the bottom centre.
    /// </summary>
    /// <param name="canvas">The Grasshopper canvas being rendered.</param>
    /// <param name="graphics">The GDI+ graphics context.</param>
    /// <param name="channel">The current rendering channel.</param>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        Bounds = _visualBounds;

        if (channel == GH_CanvasChannel.Objects)
        {
            float gripCtrX = Bounds.Left + Bounds.Width / 2f;
            float gripCtrY = Bounds.Y + Bounds.Height;
            var gripRadius = 4f;
            var circleBounds = new RectangleF(gripCtrX - gripRadius, gripCtrY - 4f, gripRadius * 2, gripRadius * 2);
            using var fill = new SolidBrush(Color.White);
            using var border = new Pen(Color.Black, 2f);
            graphics.FillEllipse(fill, circleBounds);
            graphics.DrawEllipse(border, circleBounds);
        }

        base.Render(canvas, graphics, channel);
        Bounds = _gripBounds;
    }
}
