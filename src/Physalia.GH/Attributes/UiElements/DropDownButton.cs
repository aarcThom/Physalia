// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using Grasshopper.Kernel;

namespace Physalia.GH.Attributes.UiElements;

/// <summary>
/// A button rendered on the GH canvas that shows a selected value and a dropdown caret.
/// Text is truncated with an ellipsis when it overflows the available width.
/// Draws itself on construction via the supplied <see cref="Graphics"/> context.
/// </summary>
public class DropDownButton
{
    private const float CaretAreaWidth = 20f;
    private const float TextPadding = 6f;

    private static readonly StringFormat _textFmt = new StringFormat
    {
        // Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="DropDownButton"/> class and draws
    /// the button immediately into <paramref name="graphics"/>.
    /// </summary>
    /// <param name="bounds">Bounding rectangle of the button in canvas space.</param>
    /// <param name="owner">Owning component — drives the outline colour.</param>
    /// <param name="graphics">GDI+ graphics context to draw into.</param>
    /// <param name="buttonText">Text to display; truncated with '…' when too long.</param>
    /// <param name="backgroundBrush">Pre-created brush for the button fill.</param>
    public DropDownButton(RectangleF bounds, IGH_Component owner, Graphics graphics, string buttonText, Brush backgroundBrush)
    {
        DrawBackground(graphics, bounds, backgroundBrush, owner);
        DrawCaret(graphics, bounds);
        DrawText(graphics, bounds, buttonText);
    }

    private static void DrawBackground(Graphics g, RectangleF bounds, Brush fill, IGH_Component owner)
    {
        g.FillRectangle(fill, bounds);
        using var pen = OutlinePen(owner);
        g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static Pen OutlinePen(IGH_Component owner) =>
        owner.RuntimeMessageLevel switch
        {
            GH_RuntimeMessageLevel.Warning => new Pen(Color.FromArgb(255, 80, 10, 0)),
            GH_RuntimeMessageLevel.Error   => new Pen(Color.FromArgb(255, 60, 0, 0)),
            _                              => new Pen(Color.FromArgb(255, 50, 50, 50)),
        };

    private static void DrawCaret(Graphics g, RectangleF bounds)
    {
        float cx = bounds.Right - CaretAreaWidth / 2f;
        float cy = bounds.Top + (bounds.Height / 2f);
        var tri = new PointF[]
        {
            new(cx - 4f, cy - 3f),
            new(cx + 4f, cy - 3f),
            new(cx,      cy + 3f),
        };
        g.FillPolygon(Brushes.White, tri);
    }

    private static void DrawText(Graphics g, RectangleF bounds, string text)
    {
        var textRect = new RectangleF(
            bounds.Left + TextPadding,
            bounds.Top,
            bounds.Width - CaretAreaWidth - TextPadding,
            bounds.Height);
        g.DrawString(text, GH_FontServer.StandardAdjusted, Brushes.White, textRect, _textFmt);
    }
}
