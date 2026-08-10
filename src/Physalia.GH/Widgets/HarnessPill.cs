// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.Kernel;

namespace Physalia.GH.Widgets;

/// <summary>
/// The little glyph a harness pill leads with.
/// </summary>
internal enum PillGlyph
{
    /// <summary>Points back the way you came — the return pill.</summary>
    LeftArrow,

    /// <summary>Points down at the menu the pill opens.</summary>
    DownArrow,
}

/// <summary>
/// Shared look and layout for the pill widgets stacked at the top-left of the canvas while you are
/// inside a harness document.
///
/// <para>They form one column, so the geometry and palette live here rather than in each widget: a
/// row index is all a widget needs to sit correctly under the one above it, and the two cannot drift
/// out of alignment or out of colour as either is edited.</para>
///
/// <para>All measurements are DEVICE (screen) pixels. Widget rendering runs under the canvas
/// pan/zoom transform, so a pill must reset the transform before drawing — see
/// <see cref="Draw"/> — or it would swim with the canvas instead of staying pinned to the corner.</para>
/// </summary>
internal static class HarnessPill
{
    /// <summary>Left inset of the column.</summary>
    internal const int LeftOffset = 14;

    /// <summary>Top inset of the first row.</summary>
    internal const int TopOffset = 14;

    /// <summary>Height of one pill.</summary>
    internal const int Height = 30;

    /// <summary>Vertical space between stacked pills.</summary>
    internal const int Gap = 8;

    private const int PaddingX = 14;
    private const int CornerRadius = 8;
    private const int GlyphWidth = 10;
    private const int GlyphHalfHeight = 6;
    private const int GlyphTextGap = 6;

    private static readonly Color PillFill = Color.FromArgb(255, 218, 243, 245);
    private static readonly Color PillEdge = Color.FromArgb(255, 47, 8, 87);
    private static readonly Color PillText = Color.FromArgb(255, 47, 8, 87);

    /// <summary>Gets the ink colour, for a widget drawing its own Widgets-menu icon to match.</summary>
    internal static Color Ink => PillEdge;

    /// <summary>
    /// Measures the pill for a label, placed in the given row of the column.
    /// </summary>
    /// <param name="graphics">Graphics to measure the text with.</param>
    /// <param name="label">The pill's label.</param>
    /// <param name="row">Zero-based row: 0 is the top pill, 1 sits under it, and so on.</param>
    /// <returns>The pill's frame in device pixels.</returns>
    internal static Rectangle Measure(Graphics graphics, string label, int row)
    {
        int textWidth = (int)graphics.MeasureString(label, GH_FontServer.Standard).Width;
        return new Rectangle(
            LeftOffset,
            TopOffset + (row * (Height + Gap)),
            (PaddingX * 2) + GlyphWidth + GlyphTextGap + textWidth,
            Height);
    }

    /// <summary>
    /// Draws a pill: rounded body, then the glyph and label. Resets the canvas transform for the
    /// duration so the pill is pinned to the window corner regardless of pan and zoom, and restores
    /// it afterwards.
    /// </summary>
    /// <param name="graphics">The canvas graphics.</param>
    /// <param name="frame">The frame from <see cref="Measure"/>.</param>
    /// <param name="label">The pill's label.</param>
    /// <param name="glyph">Which glyph to lead with.</param>
    internal static void Draw(Graphics graphics, Rectangle frame, string label, PillGlyph glyph)
    {
        Matrix oldTransform = graphics.Transform;
        SmoothingMode oldMode = graphics.SmoothingMode;
        graphics.ResetTransform();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (GraphicsPath pill = RoundedRect(frame, CornerRadius))
        using (var fill = new SolidBrush(PillFill))
        using (var edge = new Pen(PillEdge, 1f))
        {
            graphics.FillPath(fill, pill);
            graphics.DrawPath(edge, pill);
        }

        using (var ink = new SolidBrush(PillText))
        {
            float midY = frame.Y + (frame.Height / 2f);
            float glyphX = frame.X + PaddingX;

            // Glyphs are drawn as triangles rather than typeset, so they render identically whatever
            // fonts happen to be installed.
            graphics.FillPolygon(ink, glyph == PillGlyph.LeftArrow
                ? new[]
                {
                    new PointF(glyphX, midY),
                    new PointF(glyphX + GlyphWidth, midY - GlyphHalfHeight),
                    new PointF(glyphX + GlyphWidth, midY + GlyphHalfHeight),
                }
                : new[]
                {
                    new PointF(glyphX, midY - (GlyphHalfHeight / 2f)),
                    new PointF(glyphX + GlyphWidth, midY - (GlyphHalfHeight / 2f)),
                    new PointF(glyphX + (GlyphWidth / 2f), midY + GlyphHalfHeight),
                });

            SizeF textSize = graphics.MeasureString(label, GH_FontServer.Standard);
            graphics.DrawString(
                label,
                GH_FontServer.Standard,
                ink,
                glyphX + GlyphWidth + GlyphTextGap,
                midY - (textSize.Height / 2f));
        }

        graphics.SmoothingMode = oldMode;
        graphics.Transform = oldTransform;
        oldTransform.Dispose();
    }

    /// <summary>
    /// Builds a 24x24 Widgets-menu icon showing one of the pill glyphs.
    /// </summary>
    /// <param name="glyph">The glyph to draw.</param>
    /// <returns>The icon bitmap.</returns>
    internal static Bitmap CreateIcon(PillGlyph glyph)
    {
        var bitmap = new Bitmap(24, 24);
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var ink = new SolidBrush(PillEdge);

        g.FillPolygon(ink, glyph == PillGlyph.LeftArrow
            ? new[] { new PointF(6f, 12f), new PointF(17f, 4f), new PointF(17f, 20f) }
            : new[] { new PointF(4f, 8f), new PointF(20f, 8f), new PointF(12f, 19f) });

        return bitmap;
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
