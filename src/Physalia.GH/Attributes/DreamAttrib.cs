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

public class DreamAttrib : GH_ComponentAttributes
{
    private readonly Dream _dream;

    private RectangleF _providerRowBounds;  // full row hit area
    private RectangleF _modelRowBounds;

    private const float RowHeight = 26f;
    private const float IconWidth = 44f;
    private const float LabelWidth = 50f;
    private const float Padding = 6f;
    public DreamAttrib(Dream dream) : base(dream) 
    {
        _dream = dream;
    }

    protected override void Layout()
    {
        float width = 200f;
        float height = Padding + RowHeight + RowHeight + Padding;
        Bounds = new RectangleF(Pivot.X - width / 2f, Pivot.Y - height / 2f, width, height);

        float rowY1 = Bounds.Y + Padding;
        float rowY2 = rowY1 + RowHeight;
        _providerRowBounds = new RectangleF(Bounds.X, rowY1, Bounds.Width, RowHeight);
        _modelRowBounds = new RectangleF(Bounds.X, rowY2, Bounds.Width, RowHeight);
    }

    protected override void Render(GH_Canvas canvas, Graphics g, GH_CanvasChannel channel)
    {
        if (channel != GH_CanvasChannel.Objects)
        {
            base.Render(canvas, g, channel);
            return;
        }

        // 1. Background capsule
        var capsule = GH_Capsule.CreateCapsule(Bounds, GH_Palette.Normal);
        capsule.AddOutputGrip(Bounds.Y + Bounds.Height / 2f); // single output nub
        capsule.Render(g, Selected, Owner.Locked, false);
        capsule.Dispose();

        // 2. Divider between the two rows
        float divY = Bounds.Y + Padding + RowHeight;
        g.DrawLine(Pens.LightGray, Bounds.X + IconWidth, divY, Bounds.Right - 2f, divY);

        // 3. Vertical divider between label and value columns
        float colX = Bounds.X + IconWidth + LabelWidth;
        g.DrawLine(Pens.LightGray, colX, Bounds.Y + Padding, colX, Bounds.Bottom - Padding);

        // 4. Icon placeholder (red circle) — swap for real icon later
        var iconRect = new RectangleF(Bounds.X + 6f, Bounds.Y + Padding + 4f,
                                      IconWidth - 12f, Bounds.Height - Padding * 2f - 4f);
        g.FillEllipse(Brushes.Red, iconRect);

        // 5. Draw both rows
        DrawRow(g, _providerRowBounds, "Provider", _dream.SelectedProvider);
        DrawRow(g, _modelRowBounds, "Model", _dream.SelectedModel);
    }

    private void DrawRow(Graphics g, RectangleF row, string label, string value)
    {
        var labelRect = new RectangleF(row.X + IconWidth, row.Y, LabelWidth, row.Height);
        var colX = row.X + IconWidth + LabelWidth;
        float arrowW = 20f;
        var valueRect = new RectangleF(colX, row.Y, row.Width - IconWidth - LabelWidth - arrowW, row.Height);
        var arrowRect = new RectangleF(row.Right - arrowW, row.Y, arrowW, row.Height);

        var fmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        g.DrawString(label, GH_FontServer.StandardAdjusted, Brushes.Black, labelRect, fmt);
        g.DrawString(value, GH_FontServer.StandardAdjusted, Brushes.Black, valueRect, fmt);

        // Dropdown arrow triangle
        float cx = arrowRect.X + arrowRect.Width / 2f;
        float cy = arrowRect.Y + arrowRect.Height / 2f;
        var tri = new PointF[]
        {
          new(cx - 5f, cy - 3f),
          new(cx + 5f, cy - 3f),
          new(cx,      cy + 4f)
        };
        g.FillPolygon(Brushes.DimGray, tri);
    }

    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button != System.Windows.Forms.MouseButtons.Left)
            return base.RespondToMouseDown(sender, e);

        if (_providerRowBounds.Contains(e.CanvasLocation))
        {
            ShowProviderMenu();
            return GH_ObjectResponse.Handled;
        }
        if (_modelRowBounds.Contains(e.CanvasLocation))
        {
            ShowModelMenu();
            return GH_ObjectResponse.Handled;
        }
        return base.RespondToMouseDown(sender, e);
    }

    private void ShowProviderMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        foreach (var p in _dream.AvailableProviders)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(p);
            item.Click += (s, e) =>
            {
                _dream.SelectedProvider = p;
                _dream.SelectedModel = "";
                _dream.ExpireSolution(true);
            };
            menu.Items.Add(item);
        }
        menu.Show(System.Windows.Forms.Cursor.Position);
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