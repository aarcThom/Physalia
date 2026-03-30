// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the Prompt component. A panel-inspired prompting interface
/// divided into three vertically stacked sections: title, history, and entry.
/// </summary>
public class PromptAttrib : GH_ComponentAttributes
{
    // NESTED TYPES =======================================================================================

    // identifies which resize grip is currently being dragged
    private enum ResizeTarget { None, A2, A3 }

    // FIELDS =======================================================================================

    private readonly Prompt _prompt; // the prompt component
    private string _promptText;      // the actual prompt text

    // sizing state — persisted across saves
    private float _width;
    private float _a2Height; // history section
    private float _a3Height; // entry section

    // computed section rectangles — rebuilt in Layout()
    private RectangleF _boundsTitle;
    private RectangleF _boundsA2;
    private RectangleF _boundsA3;
    private RectangleF _gripA2;
    private RectangleF _gripA3;

    // resize drag state
    private ResizeTarget _activeGrip;
    private PointF _resizeStart;
    private float _widthAtStart;
    private float _a2HeightAtStart;
    private float _a3HeightAtStart;

    // CONSTANTS =======================================================================================

    private const float TitleHeight = 30f;
    private const float Gap = 4f;
    private const float GripSize = 14f;
    private const float CornerRadius = 4f;
    private const float MinWidth = 140f;
    private const float MinSectionHeight = 40f;
    private const float DefaultWidth = 220f;
    private const float DefaultA2Height = 120f;
    private const float DefaultA3Height = 80f;

    // CONSTRUCTOR =======================================================================================

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptAttrib"/> class.
    /// </summary>
    /// <param name="prompt">The Prompt component that owns these attributes.</param>
    public PromptAttrib(Prompt prompt)
        : base(prompt)
    {
        _prompt = prompt;
    }

    // PUBLIC METHODS =======================================================================================

    /// <summary>
    /// Persists the panel section sizes so they survive save and reload.
    /// </summary>
    /// <param name="writer">The GH_IWriter to write to.</param>
    /// <returns>true.</returns>
    public override bool Write(GH_IWriter writer)
    {
        writer.SetDouble("Width", _width);
        writer.SetDouble("A2Height", _a2Height);
        writer.SetDouble("A3Height", _a3Height);
        return base.Write(writer);
    }

    /// <summary>
    /// Restores the panel section sizes on load.
    /// </summary>
    /// <param name="reader">The GH_IReader to read from.</param>
    /// <returns>true.</returns>
    public override bool Read(GH_IReader reader)
    {
        double w = DefaultWidth, a2h = DefaultA2Height, a3h = DefaultA3Height;
        reader.TryGetDouble("Width", ref w);
        reader.TryGetDouble("A2Height", ref a2h);
        reader.TryGetDouble("A3Height", ref a3h);
        _width = (float)w;
        _a2Height = (float)a2h;
        _a3Height = (float)a3h;
        return base.Read(reader);
    }

    // PROTECTED METHODS =======================================================================================

    /// <summary>
    /// Computes the three section rectangles and resize grips from the current pivot and stored sizes.
    /// </summary>
    protected override void Layout()
    {
        if (_width < MinWidth)
        {
            _width = DefaultWidth;
            _a2Height = DefaultA2Height;
            _a3Height = DefaultA3Height;
        }

        float x = Pivot.X;
        float y = Pivot.Y;

        _boundsTitle = new RectangleF(x, y, _width, TitleHeight);
        _boundsA2    = new RectangleF(x, y + TitleHeight + Gap, _width, _a2Height);
        _boundsA3    = new RectangleF(x, y + TitleHeight + Gap + _a2Height + Gap, _width, _a3Height);

        Bounds = new RectangleF(x, y, _width, TitleHeight + Gap + _a2Height + Gap + _a3Height);

        _gripA2 = new RectangleF(_boundsA2.Right - GripSize, _boundsA2.Bottom - GripSize, GripSize, GripSize);
        _gripA3 = new RectangleF(_boundsA3.Right - GripSize, _boundsA3.Bottom - GripSize, GripSize, GripSize);

        LayoutOutputParam();
    }

    /// <summary>
    /// Renders the three panel sections and their resize grips in the Objects channel;
    /// delegates all other channels to the base implementation.
    /// </summary>
    /// <param name="canvas">The Grasshopper canvas being rendered.</param>
    /// <param name="graphics">The GDI+ graphics context.</param>
    /// <param name="channel">The current rendering channel.</param>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (channel == GH_CanvasChannel.Objects)
        {
            DrawSection(graphics, _boundsTitle);
            DrawSection(graphics, _boundsA2);
            DrawSection(graphics, _boundsA3);
            DrawResizeGrip(graphics, _gripA2);
            DrawResizeGrip(graphics, _gripA3);
            return;
        }

        base.Render(canvas, graphics, channel);
    }

    /// <summary>
    /// Captures a resize drag when the user presses the mouse button inside either resize grip.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Capture if a grip was hit; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_gripA2.Contains(e.CanvasLocation))
            {
                _activeGrip = ResizeTarget.A2;
                _resizeStart = e.CanvasLocation;
                _widthAtStart = _width;
                _a2HeightAtStart = _a2Height;
                return GH_ObjectResponse.Capture;
            }

            if (_gripA3.Contains(e.CanvasLocation))
            {
                _activeGrip = ResizeTarget.A3;
                _resizeStart = e.CanvasLocation;
                _widthAtStart = _width;
                _a3HeightAtStart = _a3Height;
                return GH_ObjectResponse.Capture;
            }
        }

        return base.RespondToMouseDown(sender, e);
    }

    /// <summary>
    /// Updates section sizes during a resize drag; shows the resize cursor when hovering over either grip.
    /// Horizontal drag always resizes the shared width; vertical drag resizes only the dragged section.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if a resize drag is in progress; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_activeGrip != ResizeTarget.None)
        {
            float dx = e.CanvasLocation.X - _resizeStart.X;
            float dy = e.CanvasLocation.Y - _resizeStart.Y;

            _width = Math.Max(MinWidth, _widthAtStart + dx);

            if (_activeGrip == ResizeTarget.A2)
                _a2Height = Math.Max(MinSectionHeight, _a2HeightAtStart + dy);
            else
                _a3Height = Math.Max(MinSectionHeight, _a3HeightAtStart + dy);

            ExpireLayout();
            sender.ScheduleRegen(2);
            return GH_ObjectResponse.Handled;
        }

        bool overGrip = _gripA2.Contains(e.CanvasLocation) || _gripA3.Contains(e.CanvasLocation);
        sender.Cursor = overGrip ? Cursors.SizeNWSE : Cursors.Default;
        return base.RespondToMouseMove(sender, e);
    }

    /// <summary>
    /// Ends a resize drag.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Release if a drag was in progress; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_activeGrip != ResizeTarget.None)
        {
            _activeGrip = ResizeTarget.None;
            return GH_ObjectResponse.Release;
        }

        return base.RespondToMouseUp(sender, e);
    }

    // PRIVATE METHODS =======================================================================================

    // positions the output param at the right edge of the history section, vertically centred
    private void LayoutOutputParam()
    {
        var param = Owner.Params.Output[0];
        float midY = _boundsA2.Y + _boundsA2.Height / 2f;
        param.Attributes.Pivot = new PointF(Bounds.Right, midY);
        param.Attributes.Bounds = new RectangleF(Bounds.Right - 5f, midY - 5f, 10f, 10f);
    }

    private void DrawSection(Graphics graphics, RectangleF bounds)
    {
        using var path = RoundedRect(bounds, CornerRadius);
        using var fill = new SolidBrush(Color.FromArgb(255, 250, 240, 190));
        using var border = new Pen(Color.FromArgb(180, 120, 100, 60), 1f);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
    }

    private void DrawResizeGrip(Graphics graphics, RectangleF grip)
    {
        // 3 diagonal lines in the bottom-right corner of the section
        using var pen = new Pen(Color.FromArgb(140, 100, 80, 50), 1.5f);
        float x = grip.Right;
        float y = grip.Bottom;
        for (int i = 1; i <= 3; i++)
        {
            float offset = i * 4f;
            graphics.DrawLine(pen, x - offset, y - 2f, x - 2f, y - offset);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
