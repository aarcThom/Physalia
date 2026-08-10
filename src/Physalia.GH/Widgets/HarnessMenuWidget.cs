// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Widgets;
using Grasshopper.Kernel;
using Physalia.GH.Harness;

namespace Physalia.GH.Widgets;

/// <summary>
/// The harness's own menu, shown directly beneath the return pill while you are inside a harness
/// document — the second pill in the harness column (see <see cref="HarnessPill"/>).
///
/// <para>It carries the actions that apply to the harness you are standing in, so they are to hand
/// without leaving it to right-click the proxy on the host canvas. Right now that is saving the
/// pipeline to the preset library; the widget is a menu rather than a button so the next one costs a
/// line.</para>
/// </summary>
public sealed class HarnessMenuWidget : GH_Widget
{
    private const string Label = "Harness";

    // Row 1 of the top-left harness column: directly under the return pill.
    private const int Row = 1;

    // Last-rendered pill in device pixels, reused for hit-testing and for anchoring the menu.
    private Rectangle _frame;

    private Bitmap? _icon;

    /// <inheritdoc/>
    public override string Name => "Physalia Harness Menu";

    /// <inheritdoc/>
    public override string Description => "Actions for the harness you are inside — saving it as a preset.";

    /// <inheritdoc/>
    public override string TooltipText => "Harness actions.";

    /// <inheritdoc/>
    public override bool TooltipEnabled => true;

    /// <inheritdoc/>
    /// <remarks>
    /// Always on, like the return pill it sits under: the two are one control surface, and hiding
    /// half a column would read as a bug. Neither draws outside a harness, so on an ordinary canvas
    /// there is nothing to switch off.
    /// </remarks>
    public override bool Visible
    {
        get => true;
        set { }
    }

    /// <inheritdoc/>
    /// <remarks>Drawn rather than embedded: one glyph, never themed.</remarks>
    public override Bitmap Icon_24x24 => _icon ??= HarnessPill.CreateIcon(PillGlyph.DownArrow);

    /// <summary>
    /// Draws the menu pill when the canvas is showing a harness document, and nothing otherwise.
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
        HarnessPill.Draw(canvas.Graphics, _frame, Label, PillGlyph.DownArrow);
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
    /// Drops the harness menu open under the pill when it is pressed.
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

        if (HarnessComponent.OwnerOf(sender?.Document) is not { } harness)
        {
            return GH_ObjectResponse.Ignore;
        }

        // WinForms, not Eto: an Eto.ContextMenu does not show on the Grasshopper canvas.
        var menu = new ContextMenuStrip();
        menu.Items.Add(HarnessComponent.SavePresetLabel, null, (_, _) => harness.SaveAsPreset());

        // Anchored under the pill rather than at the cursor, so it reads as this pill's menu. The
        // item's handler runs after the menu closes, which is why it is safe for it to show dialogs.
        menu.Show(sender, new Point(_frame.Left, _frame.Bottom + 2));
        return GH_ObjectResponse.Handled;
    }
}
