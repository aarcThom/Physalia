// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Widgets;
using Grasshopper.Kernel;
using Physalia.GH.Harness;

namespace Physalia.GH.Widgets;

/// <summary>
/// A back button shown at the top-left of the canvas while you are inside a harness document.
///
/// <para>Grasshopper has no sub-document navigation UI — not for clusters either. All it offers is
/// a relabelled File menu entry ("Save and Return") and the document dropdown, and that entry takes
/// the destructive path: it removes the sub-document from the document server, which disposes it.
/// This widget is the non-destructive way out, leaving the pipeline running.</para>
/// </summary>
public sealed class HarnessReturnWidget : GH_Widget
{
    // Pill geometry in device (screen) pixels, docked to the top-left of the canvas.
    private const int LeftOffset = 14;
    private const int TopOffset = 14;
    private const int Height = 30;
    private const int PaddingX = 14;
    private const int ArrowWidth = 10;
    private const int CornerRadius = 8;

    private static readonly Color PillFill = Color.FromArgb(255, 218, 243, 245);
    private static readonly Color PillEdge = Color.FromArgb(255, 47, 8, 87);
    private static readonly Color PillText = Color.FromArgb(255, 47, 8, 87);

    // Last-rendered pill in device pixels, reused for hit-testing.
    private Rectangle _frame;

    private Bitmap? _icon;

    /// <inheritdoc/>
    public override string Name => "Physalia Harness Return";

    /// <inheritdoc/>
    public override string Description => "Leave the harness and return to the document it sits on.";

    /// <inheritdoc/>
    public override string TooltipText => "Return to the host document.";

    /// <inheritdoc/>
    public override bool TooltipEnabled => true;

    /// <inheritdoc/>
    /// <remarks>
    /// Always on. This is the only non-destructive way out of a harness, so it is not something the
    /// user should be able to switch off from the canvas Widgets menu and strand themselves.
    /// </remarks>
    public override bool Visible
    {
        get => true;
        set { }
    }

    /// <inheritdoc/>
    /// <remarks>Drawn rather than embedded: it is a single glyph and never needs to be themed.</remarks>
    public override Bitmap Icon_24x24 => _icon ??= CreateIcon();

    /// <summary>
    /// Draws the back pill when the canvas is showing a harness document, and nothing otherwise.
    /// </summary>
    /// <param name="canvas">The canvas being painted.</param>
    public override void Render(GH_Canvas canvas)
    {
        _frame = Rectangle.Empty;

        if (canvas?.Graphics is null || HarnessOf(canvas) is null)
        {
            return;
        }

        Graphics g = canvas.Graphics;

        // Widget Render runs under the canvas pan/zoom transform — reset to device space so the
        // pill is pinned to the window corner regardless of pan and zoom.
        Matrix oldTransform = g.Transform;
        SmoothingMode oldMode = g.SmoothingMode;
        g.ResetTransform();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        const string label = "Back to document";
        Font font = GH_FontServer.Standard;
        int textWidth = (int)g.MeasureString(label, font).Width;
        _frame = new Rectangle(LeftOffset, TopOffset, (PaddingX * 2) + ArrowWidth + 6 + textWidth, Height);

        using (GraphicsPath pill = RoundedRect(_frame, CornerRadius))
        using (var fill = new SolidBrush(PillFill))
        using (var edge = new Pen(PillEdge, 1f))
        {
            g.FillPath(fill, pill);
            g.DrawPath(edge, pill);
        }

        using (var ink = new SolidBrush(PillText))
        {
            float midY = _frame.Y + (_frame.Height / 2f);
            float arrowX = _frame.X + PaddingX;

            // A left-pointing triangle, drawn rather than glyphed so it renders identically
            // regardless of the installed fonts.
            g.FillPolygon(ink, new[]
            {
                new PointF(arrowX, midY),
                new PointF(arrowX + ArrowWidth, midY - 6f),
                new PointF(arrowX + ArrowWidth, midY + 6f),
            });

            SizeF textSize = g.MeasureString(label, font);
            g.DrawString(label, font, ink, arrowX + ArrowWidth + 6f, midY - (textSize.Height / 2f));
        }

        g.SmoothingMode = oldMode;
        g.Transform = oldTransform;
        oldTransform.Dispose();
    }

    /// <summary>
    /// Hit-tests a point against the rendered pill.
    /// </summary>
    /// <param name="pt_control">The point in control (device) coordinates.</param>
    /// <param name="pt_canvas">The point in canvas (world) coordinates.</param>
    /// <returns>true when the point is inside the pill.</returns>
    public override bool Contains(Point pt_control, PointF pt_canvas)
        => !_frame.IsEmpty && _frame.Contains(pt_control);

    /// <summary>
    /// Returns to the host document when the pill is pressed.
    /// </summary>
    /// <param name="sender">The canvas the mouse event originated from.</param>
    /// <param name="e">The mouse event.</param>
    /// <returns>Handled when the press landed on the pill, otherwise Ignore.</returns>
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_frame.IsEmpty || !_frame.Contains(e.ControlLocation))
        {
            return GH_ObjectResponse.Ignore;
        }

        HarnessOf(sender)?.ReturnToHost();
        return GH_ObjectResponse.Handled;
    }

    // The harness owning whatever document the canvas is showing, or null when the canvas is on an
    // ordinary document.
    private static HarnessComponent? HarnessOf(GH_Canvas? canvas) => HarnessComponent.OwnerOf(canvas?.Document);

    // The Widgets-menu icon: the same left-pointing triangle the pill draws.
    private static Bitmap CreateIcon()
    {
        var bitmap = new Bitmap(24, 24);
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var ink = new SolidBrush(PillEdge);
        g.FillPolygon(ink, new[]
        {
            new PointF(6f, 12f),
            new PointF(17f, 4f),
            new PointF(17f, 20f),
        });

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
