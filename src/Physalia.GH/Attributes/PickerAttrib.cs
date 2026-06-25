// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Components.Utility;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the <see cref="Picker"/> component that render an inline dropdown
/// button on the Grasshopper canvas showing the currently selected value.
/// Left-clicking the button opens the selection menu.
/// </summary>
public class PickerAttrib : PhyComponentAttributes
{
    private const float MinWidth = 120f;
    private const float ButtonPadding = 4f;
    private const float IconGap = 34f; // 24px icon + 10px gap

    private RectangleF _buttonBounds;
    private LinearGradientBrush? _backgroundBrush;

    /// <summary>
    /// Initializes a new instance of the <see cref="PickerAttrib"/> class.
    /// </summary>
    /// <param name="picker">The <see cref="Picker"/> component that owns these attributes.</param>
    public PickerAttrib(Picker picker)
        : base(picker)
    {
    }

    /// <inheritdoc/>
    protected override void Layout()
    {
        // PhyComponentAttributes handles the collapse guard and the normal GH layout.
        base.Layout();

        if (IsHarnessCollapsed)
        {
            _buttonBounds = RectangleF.Empty;
            _backgroundBrush?.Dispose();
            _backgroundBrush = null;
            return;
        }

        // Enforce a minimum width for the dropdown button.
        if (Bounds.Width < MinWidth)
            Bounds = new RectangleF(Bounds.X, Bounds.Y, MinWidth, Bounds.Height);

        // Reposition the output grip to the (possibly expanded) right edge.
        var op = Owner.Params.Output[0];
        float midY = Bounds.Y + (Bounds.Height / 2f);
        op.Attributes.Pivot = new PointF(Bounds.Right, midY);
        op.Attributes.Bounds = new RectangleF(Bounds.Right, Bounds.Y, 0f, Bounds.Height);

        // Button fills the interior to the right of the icon.
        float btnX = Bounds.Left + IconGap;
        float btnWidth = Bounds.Right - btnX - ButtonPadding;
        float btnHeight = Bounds.Height - (ButtonPadding * 2f);
        _buttonBounds = new RectangleF(btnX, Bounds.Y + ButtonPadding, btnWidth, btnHeight);

        _backgroundBrush?.Dispose();
        _backgroundBrush = null;
        if (btnHeight > 0)
        {
            _backgroundBrush = new LinearGradientBrush(
                new PointF(0, _buttonBounds.Top),
                new PointF(0, _buttonBounds.Bottom),
                Color.DarkGray,
                Color.Black);
        }
    }

    /// <inheritdoc/>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        // PhyComponentAttributes' render honours the collapse guard; when collapsed the button
        // bounds and brush are cleared in Layout, so the dropdown below does not draw either.
        base.Render(canvas, graphics, channel);

        if (channel == GH_CanvasChannel.Objects && _backgroundBrush != null)
        {
            var picker = (Picker)Owner;
            string label = string.IsNullOrEmpty(picker.SelectedValue) ? "—" : picker.SelectedValue;
            new DropDownButton(_buttonBounds, Owner, graphics, label, _backgroundBrush);
        }
    }

    /// <inheritdoc/>
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button == MouseButtons.Left && _buttonBounds.Contains(e.CanvasLocation))
        {
            ShowSelectionMenu();
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseDown(sender, e);
    }

    private void ShowSelectionMenu()
    {
        var picker = (Picker)Owner;
        var values = picker.AvailableValues;
        if (values.Count == 0) return;

        var menu = new ContextMenuStrip();
        foreach (var value in values)
        {
            var item = new ToolStripMenuItem(value)
            {
                Checked = value == picker.SelectedValue,
            };
            item.Click += (s, e) =>
            {
                picker.SetSelectedValue(value);
                picker.ExpireSolution(true);
            };
            menu.Items.Add(item);
        }

        menu.Show(Cursor.Position);
    }
}
