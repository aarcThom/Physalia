// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#if WINDOWS
using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace Physalia.GH.Widgets;

/// <summary>
/// Draws a persistent screen-space HUD banner on the Grasshopper canvas while the Serializer
/// selection interaction is active.
/// </summary>
internal static class SerializeWidget
{
    private const string BannerText =
        "Serializer — select the objects to export, then press Enter to confirm  (Esc to cancel)";

    /// <summary>
    /// Subscribes the widget painter to <paramref name="canvas"/>.
    /// </summary>
    /// <param name="canvas">The canvas to paint on.</param>
    public static void Attach(GH_Canvas canvas) =>
        canvas.CanvasPostPaintWidgets += Draw;

    /// <summary>
    /// Unsubscribes the widget painter from <paramref name="canvas"/> and repaints.
    /// </summary>
    /// <param name="canvas">The canvas to detach from.</param>
    public static void Detach(GH_Canvas canvas)
    {
        canvas.CanvasPostPaintWidgets -= Draw;
        canvas.Refresh();
    }

    /// <summary>
    /// Paints the selection-prompt banner in the top-left corner of the canvas window.
    /// </summary>
    /// <param name="sender">The canvas being painted.</param>
    private static void Draw(GH_Canvas sender)
    {
        if (sender?.Graphics is null)
        {
            return;
        }

        Graphics g = sender.Graphics;

        // CanvasPostPaintWidgets fires under the pan/zoom transform. Reset to device space so
        // the banner is pinned to the top-left of the window regardless of pan/zoom.
        Matrix oldTransform = g.Transform;
        SmoothingMode oldMode = g.SmoothingMode;
        g.ResetTransform();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Font font = GH_FontServer.Standard;
        SizeF textSize = g.MeasureString(BannerText, font);
        const float margin = 10f;
        float boxWidth = textSize.Width + 24f;
        float boxHeight = textSize.Height + 14f;
        var box = new RectangleF(margin, margin, boxWidth, boxHeight);

        using (var background = new SolidBrush(Color.FromArgb(235, 30, 30, 30)))
        using (var border = new Pen(Color.FromArgb(255, 255, 191, 0), 1.5f))
        using (var foreground = new SolidBrush(Color.White))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.FillRectangle(background, box);
            g.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
            g.DrawString(BannerText, font, foreground, box, format);
        }

        g.SmoothingMode = oldMode;
        g.Transform = oldTransform;
        oldTransform.Dispose();
    }
}
#endif
