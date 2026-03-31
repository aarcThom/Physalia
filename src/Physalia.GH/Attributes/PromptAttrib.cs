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
    private enum ResizeTarget { None, convoTarget, inputTarget }

    // FIELDS =======================================================================================

    private readonly Prompt _prompt; // the prompt component
    private string _promptText;      // the actual prompt text

    // sizing state — persisted across saves
    private float _width;
    private float _convoHeight; // history section
    private float _inputHeight; // entry section

    // computed section rectangles — rebuilt in Layout()
    private RectangleF _boundsTitle;
    private RectangleF _boundsConvo;
    private RectangleF _boundsInput;
    private RectangleF _gripConvo;
    private RectangleF _gripInput;

    // resize drag state
    private ResizeTarget _activeGrip;
    private PointF _resizeStart;
    private float _widthAtStart;
    private float _convoHeightAtStart;
    private float _inputHeightAtStart;

    // CONSTANTS =======================================================================================

    private const float TitleHeight = 18f;
    private const float GripSize = 14f;
    private const float CornerRadius = 4f;
    private const float MinWidth = 140f;
    private const float MinSectionHeight = 40f;
    private const float DefaultWidth = 220f;
    private const float DefaultConvoHeight = 120f;
    private const float DefaultInputHeight = 80f;

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
        writer.SetDouble("ConvoHeight", _convoHeight);
        writer.SetDouble("InputHeight", _inputHeight);
        return base.Write(writer);
    }

    /// <summary>
    /// Restores the panel section sizes on load.
    /// </summary>
    /// <param name="reader">The GH_IReader to read from.</param>
    /// <returns>true.</returns>
    public override bool Read(GH_IReader reader)
    {
        double w = DefaultWidth, a2h = DefaultConvoHeight, a3h = DefaultInputHeight;
        reader.TryGetDouble("Width", ref w);
        reader.TryGetDouble("ConvoHeight", ref a2h);
        reader.TryGetDouble("InputHeight", ref a3h);
        _width = (float)w;
        _convoHeight = (float)a2h;
        _inputHeight = (float)a3h;
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
            _convoHeight = DefaultConvoHeight;
            _inputHeight = DefaultInputHeight;
        }

        float x = Pivot.X;
        float y = Pivot.Y;

        _boundsTitle = new RectangleF(x, y, _width, TitleHeight);
        _boundsConvo = new RectangleF(x, y + TitleHeight, _width, _convoHeight);
        _boundsInput = new RectangleF(x, y + TitleHeight + _convoHeight, _width, _inputHeight);

        Bounds = new RectangleF(x, y, _width, TitleHeight + _convoHeight + _inputHeight);

        _gripConvo = new RectangleF(_boundsConvo.Right - GripSize, _boundsConvo.Bottom - GripSize, GripSize, GripSize);
        _gripInput = new RectangleF(_boundsInput.Right - GripSize, _boundsInput.Bottom - GripSize, GripSize, GripSize);

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
            DrawTitle(graphics, _boundsTitle);
            DrawConvo(graphics, _boundsConvo);
            DrawInput(graphics, _boundsInput);
            DrawResizeGrip(graphics, _gripConvo);
            DrawResizeGrip(graphics, _gripInput);
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
            if (_gripConvo.Contains(e.CanvasLocation))
            {
                _activeGrip = ResizeTarget.convoTarget;
                _resizeStart = e.CanvasLocation;
                _widthAtStart = _width;
                _convoHeightAtStart = _convoHeight;
                return GH_ObjectResponse.Capture;
            }

            if (_gripInput.Contains(e.CanvasLocation))
            {
                _activeGrip = ResizeTarget.inputTarget;
                _resizeStart = e.CanvasLocation;
                _widthAtStart = _width;
                _inputHeightAtStart = _inputHeight;
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

            if (_activeGrip == ResizeTarget.convoTarget)
            {
                _convoHeight = Math.Max(MinSectionHeight, _convoHeightAtStart + dy);
            }
            else
            {
                _inputHeight = Math.Max(MinSectionHeight, _inputHeightAtStart + dy);
            }

            ExpireLayout();
            sender.ScheduleRegen(2);
            return GH_ObjectResponse.Handled;
        }

        bool overGrip = _gripConvo.Contains(e.CanvasLocation) || _gripInput.Contains(e.CanvasLocation);
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
        float midY = _boundsConvo.Y + _boundsConvo.Height / 2f;
        param.Attributes.Pivot = new PointF(Bounds.Right, midY);
        param.Attributes.Bounds = new RectangleF(Bounds.Right - 5f, midY - 5f, 10f, 10f);
    }

    private void DrawTitle(Graphics graphics, RectangleF bounds)
    {
        using var path = TopRoundedRect(bounds, CornerRadius);

        var topPt = new PointF(bounds.Left, bounds.Top);
        var botPt = new PointF(bounds.Left, bounds.Bottom);
        var topColor = Color.FromArgb(255, 232, 188, 255);
        var botColor = Color.FromArgb(255, 245, 234, 250);

        using var fill = new LinearGradientBrush(topPt, botPt, topColor, botColor);
        using var border = new Pen(Color.Black, 1f);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        // drawing the little hilights
        bounds.Inflate(-1f, -1f);
        using var shinePath = TopRoundedRect(bounds, CornerRadius - 1);
        var shineGradient = new LinearGradientBrush(topPt, botPt, Color.White, Color.FromArgb(100, 255, 255, 255));
        using var shineBorder = new Pen(shineGradient, 1f);
        graphics.DrawPath(shineBorder, shinePath);
    }

    private void DrawConvo(Graphics graphics, RectangleF bounds)
    {
        using var path = new GraphicsPath();
        path.AddRectangle(bounds);
        using var fill = new SolidBrush(Color.FromArgb(255, 245, 234, 250));
        using var border = new Pen(Color.Black, 1f);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        // drawing the little hilights
        bounds.Inflate(-1f, -1f);
        using var shinePath = new GraphicsPath();
        shinePath.AddRectangle(bounds);
        var topPt = new PointF(bounds.Left, bounds.Top);
        var botPt = new PointF(bounds.Left, bounds.Bottom);

        var shineGradient = new LinearGradientBrush(topPt, botPt, Color.FromArgb(200, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
        using var shineBorder = new Pen(shineGradient, 1f);
        graphics.DrawPath(shineBorder, shinePath);
    }

    private void DrawInput(Graphics graphics, RectangleF bounds)
    {
        using var path = BottomRoundedRect(bounds, CornerRadius);
        using var fill = new SolidBrush(Color.FromArgb(255, 245, 234, 250));
        using var border = new Pen(Color.Black, 1f);
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

    private static GraphicsPath TopRoundedRect(RectangleF r, float radius, bool addLine = true)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); // top left arc
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90); // top right arc
        var bottomLeftPt = new PointF(r.Left, r.Bottom);
        var bottomRightPt = new PointF(r.Right, r.Bottom);
        path.AddLine(bottomRightPt, bottomLeftPt);

        path.CloseFigure();
        return path;
    }

    private static GraphicsPath BottomRoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();

        var topLeftPt = new PointF(r.Left, r.Top);
        var topRightPt = new PointF(r.Right, r.Top);
        path.AddLine(topLeftPt, topRightPt);

        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
