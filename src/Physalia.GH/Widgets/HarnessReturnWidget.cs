// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Widgets;
using Grasshopper.Kernel;
using Physalia.GH.Harness;

namespace Physalia.GH.Widgets;

/// <summary>
/// A back button shown at the top-left of the canvas while you are inside a harness document — the
/// first pill in the harness column (see <see cref="HarnessPill"/>).
///
/// <para>Grasshopper has no sub-document navigation UI — not for clusters either. All it offers is
/// a relabelled File menu entry ("Save and Return") and the document dropdown, and that entry takes
/// the destructive path: it removes the sub-document from the document server, which disposes it.
/// This widget is the non-destructive way out, leaving the pipeline running.</para>
/// </summary>
public sealed class HarnessReturnWidget : GH_Widget
{
    private const string Label = "Back to document";

    // Row 0 of the top-left harness column.
    private const int Row = 0;

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
    public override Bitmap Icon_24x24 => _icon ??= HarnessPill.CreateIcon(PillGlyph.LeftArrow);

    /// <summary>
    /// Draws the back pill when the canvas is showing a harness document, and nothing otherwise.
    /// </summary>
    /// <param name="canvas">The canvas being painted.</param>
    public override void Render(GH_Canvas canvas)
    {
        _frame = Rectangle.Empty;

        if (canvas?.Graphics is null || HarnessComponent.OwnerOf(canvas.Document) is null)
        {
            return;
        }

        _frame = HarnessPill.Measure(canvas.Graphics, Label, Row);
        HarnessPill.Draw(canvas.Graphics, _frame, Label, PillGlyph.LeftArrow);
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

        HarnessComponent.OwnerOf(sender?.Document)?.ReturnToHost();
        return GH_ObjectResponse.Handled;
    }
}
