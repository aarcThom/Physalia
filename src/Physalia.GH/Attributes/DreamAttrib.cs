// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Components;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the DREAM component that render two inline dropdown rows
/// (Provider and Model) directly on the Grasshopper canvas.
/// </summary>
public class DreamAttrib : GH_ComponentAttributes
{
    private readonly Dream _dream;

    private RectangleF _modelButton;

    private const float RowHeight = 26f;
    private const float IconWidth = 44f;
    private const float LabelWidth = 50f;
    private const float Padding = 6f;

    /// <summary>
    /// Initializes a new instance of the <see cref="DreamAttrib"/> class.
    /// </summary>
    /// <param name="dream">The DREAM component that owns these attributes.</param>
    public DreamAttrib(Dream dream) : base(dream)
    {
        _dream = dream;
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

        //the main component rendering channel
        if (channel == GH_CanvasChannel.Objects)
        {
            // declare the pens / brushes / pallets we will need to draw the custom objects
            // - defaults for blank / message levels
            Pen outLine = PhyPalette.BlankOutline;
            GH_Palette palette = GH_Palette.Normal;

            // retrieve the proper pens / brushes from our CompColors class
            switch (Owner.RuntimeMessageLevel)
            {
                case GH_RuntimeMessageLevel.Warning:
                    // assign warning values
                    outLine = PhyPalette.WarnOutline;
                    palette = GH_Palette.Warning;
                    break;

                case GH_RuntimeMessageLevel.Error:
                    // assign warning values
                    outLine = PhyPalette.ErrorOutline;
                    palette = GH_Palette.Error;
                    break;
            }
            graphics.FillRectangle(PhyPalette.SmallButton(_modelButton.Top, _modelButton.Bottom), _modelButton);
            var border = outLine;
            graphics.DrawRectangle(border, _modelButton);

            // Dropdown arrow triangle
            float cx = _modelButton.Left + 8f;
            float cy = _modelButton.Top + _modelButton.Height / 2f;
            var tri = new PointF[]
            {
            new (cx - 4f, cy - 4f),
            new (cx + 4f, cy - 4f),
            new (cx,      cy + 4f)
            };
            graphics.FillPolygon(Brushes.White, tri);


            // the selected model
            var labelRect = new RectangleF(_modelButton.Left + 12f, _modelButton.Top + 4f, _modelButton.Width - 12f, _modelButton.Height - 8f);

            var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString(_dream.SelectedModel, GH_FontServer.StandardAdjusted, Brushes.White, labelRect, fmt);
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
        foreach (var m in _dream.AvailableModels)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(m);
            item.Click += (s, e) =>
            {
                _dream.SelectedModel = m;
                _dream.ExpireSolution(true);
            };
            menu.Items.Add(item);
        }
        menu.Show(System.Windows.Forms.Cursor.Position);
    }
}
