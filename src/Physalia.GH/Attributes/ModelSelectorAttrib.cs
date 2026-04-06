// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Attributes.UiComponents;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the providerSelector component that render two inline dropdown rows
/// (Provider and Model) directly on the Grasshopper canvas.
/// </summary>
public class ModelSelectorAttrib : GH_ComponentAttributes
{
    private const float RowHeight = 26f;
    private const float IconWidth = 44f;
    private const float LabelWidth = 50f;
    private const float Padding = 6f;

    private readonly ModelSelector modelSelector;

    private RectangleF _modelButton;
    private LinearGradientBrush? _buttonBrush;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelSelectorAttrib"/> class.
    /// </summary>
    /// <param name="modelSelector">The providerSelector component that owns these attributes.</param>
    public ModelSelectorAttrib(ModelSelector modelSelector)
        : base(modelSelector)
    {
        this.modelSelector = modelSelector;
    }

    /// <summary>
    /// Computes the component bounds and positions the output parameter grip at the right-centre edge.
    /// </summary>
    protected override void Layout()
    {
        base.Layout();

        var ownerRectangle = GH_Convert.ToRectangle(Bounds);
        ownerRectangle.Width += 80;
        Bounds = ownerRectangle;

        // Reposition output param to the new right edge; zero-width bounds suppresses the label.
        var op = Owner.Params.Output[0];
        float midY = Bounds.Y + (Bounds.Height / 2f);
        op.Attributes.Pivot = new PointF(Bounds.Right, midY);
        op.Attributes.Bounds = new RectangleF(Bounds.Right, Bounds.Y, 0f, Bounds.Height);

        // Position the model dropdown button in the space to the right of the icon + input area.
        var inWidth = Owner.Params.InputWidth;
        var iconWidth = Owner.Icon_24x24.Width;
        var leftWidth = iconWidth + inWidth + 8;

        var modelButtonX = Bounds.Left + leftWidth;
        var modelButtonWidth = Bounds.Width - leftWidth - 4;
        var modelButtonHeight = Bounds.Height - 8;

        _modelButton = new RectangleF(modelButtonX, ownerRectangle.Y + 4, modelButtonWidth, modelButtonHeight);

        _buttonBrush?.Dispose();
        _buttonBrush = new LinearGradientBrush(new PointF(0, _modelButton.Top), new PointF(0, _modelButton.Bottom), Color.DarkGray, Color.Black);
    }

    /// <summary>
    /// Renders the capsule background, column dividers, icon placeholder, and the two dropdown rows.
    /// </summary>
    /// <param name="canvas">The Grasshopper canvas being rendered.</param>
    /// <param name="g">The GDI+ graphics context.</param>
    /// <param name="channel">The current rendering channel.</param>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        base.Render(canvas, graphics, channel); // handle the wires, draw nickname, name, etc.

        // the main component rendering channel
        if (channel == GH_CanvasChannel.Objects)
        {
            // draw the button
            if (_buttonBrush != null)
                _ = new DropDownButton(_modelButton, Owner, graphics, modelSelector.SelectedModel, _buttonBrush);
        }
    }

    /// <summary>
    /// Handles left-click events on the Provider and Model rows to show the respective dropdown menus.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if a row was clicked; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button != System.Windows.Forms.MouseButtons.Left)
        {
            return base.RespondToMouseDown(sender, e);
        }

        if (_modelButton.Contains(e.CanvasLocation))
        {
            ShowModelMenu();
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseDown(sender, e);
    }

    private void ShowModelMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        foreach (var m in modelSelector.AvailableModels)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(m);
            item.Click += (s, e) =>
            {
                modelSelector.SelectedModel = m;
                modelSelector.ExpireSolution(true);
            };
            menu.Items.Add(item);
        }
        menu.Show(System.Windows.Forms.Cursor.Position);
    }
}
