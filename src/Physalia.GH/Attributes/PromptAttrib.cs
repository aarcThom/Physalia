// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
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
    private bool inputPromptCurrent = false;      // is the user currently inputing text?
    private bool _submitting = false;             // true while a Shift+Enter submit is in flight

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
    private RectangleF _wireOutputGrip;

    private RectangleF _layoutBounds; // the actual bounds, expanded for the output wire grip
    private RectangleF _renderBounds; // the bounds that are rendered.

    // resize drag state
    private ResizeTarget _activeGrip;
    private PointF _resizeStart;
    private float _widthAtStart;
    private float _convoHeightAtStart;
    private float _inputHeightAtStart;

    // CONSTANTS =======================================================================================

    private const float TitleHeight = 18f;
    private const float GripSize = 8f;
    private const float CornerRadius = 4f;
    private const float MinWidth = 140f;
    private const float MinSectionHeight = 40f;
    private const float DefaultWidth = 220f;
    private const float DefaultConvoHeight = 120f;
    private const float DefaultInputHeight = 80f;

    private readonly Color _outlineColor = Color.FromArgb(255, 47, 8, 87);

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
        writer.SetString("PromptText", _prompt.UserPromptText ?? string.Empty);
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
        string promptText = string.Empty;
        if (reader.TryGetString("PromptText", ref promptText))
            _prompt.UserPromptText = promptText;
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

        _renderBounds = new RectangleF(x, y, _width, TitleHeight + _convoHeight + _inputHeight);
        _layoutBounds = new RectangleF(x, y, _width + 4f, TitleHeight + _convoHeight + _inputHeight);


        Bounds = _renderBounds;

        _gripConvo = new RectangleF(_boundsConvo.Right - GripSize, _boundsConvo.Bottom - GripSize, GripSize, GripSize);
        _gripInput = new RectangleF(_boundsInput.Right - GripSize, _boundsInput.Bottom - GripSize, GripSize, GripSize);

        _wireOutputGrip = new RectangleF(_boundsConvo.Right - 3f, _boundsConvo.Bottom - (_boundsConvo.Height / 2) - 4f, 8f, 8f);

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

        Bounds = _renderBounds; // SET THE BOUNDS BACK TO DEFAULT FOR RENDER PASS

        if (channel == GH_CanvasChannel.Objects)
        {
            DrawWireGrip(graphics, _wireOutputGrip);
            DrawTitle(graphics, _boundsTitle);
            DrawConvo(graphics, _boundsConvo);
            DrawInput(graphics, _boundsInput);
            DrawResizeGrip(graphics, _gripConvo);
            DrawResizeGrip(graphics, _gripInput);
            return;
        }

        base.Render(canvas, graphics, channel);

        Bounds = _layoutBounds; // reset back to layout bounds so we can grip properly
    }

    // EVENT HANDLERS ===================================================================================

    /// <summary>
    /// Opens an in-place TextBox overlay over the input section on double-click.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if the input section was double-clicked; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_boundsInput.Contains(e.CanvasLocation))
        {
            float zoom = sender.Viewport.Zoom;
            PointF origin = sender.Viewport.ProjectPoint(_boundsInput.Location);
            var tb = new TextBox
            {
                Multiline = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(245, 234, 250),
                Font = GH_FontServer.Console,
                Text = _prompt.UserPromptText,
                Bounds = new Rectangle(
                    (int)origin.X + 8, (int)origin.Y + 8,
                    (int)(_boundsInput.Width * zoom - 16),
                    (int)(_boundsInput.Height * zoom - 48)),
            };

            inputPromptCurrent = true;
            sender.Controls.Add(tb);
            tb.BringToFront();
            tb.Focus();
            tb.SelectAll();

            tb.Leave += (s, _) =>
            {
                if (!_submitting)
                {
                    // user clicked away without submitting — just persist the draft text
                    _prompt.UserPromptText = tb.Text;
                    _prompt.ExpireSolution(true);
                }

                _submitting = false;
                inputPromptCurrent = false;
                sender.Controls.Remove(tb);
            };

            // shift + enter submits the message to the conversation history
            tb.KeyDown += (s, keyArgs) =>
            {
                if (keyArgs.KeyCode == Keys.Enter && keyArgs.Shift)
                {
                    keyArgs.SuppressKeyPress = true; // prevent the newline being added
                    _prompt.UserPromptText = tb.Text;
                    _submitting = true;
                    _prompt.SubmitUserMessage(); // appends to history, clears UserPromptText, expires solution
                    sender.Focus(); // triggers Leave, which removes the TextBox
                }
            };

            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseDoubleClick(sender, e);
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

            sender.Cursor = Cursors.SizeNWSE;
            ExpireLayout();
            sender.ScheduleRegen(2);
            return GH_ObjectResponse.Handled;
        }

        bool overGrip = _gripConvo.Contains(e.CanvasLocation) || _gripInput.Contains(e.CanvasLocation);
        if (overGrip)
        {
            sender.Cursor = Cursors.SizeNWSE;
            return GH_ObjectResponse.Handled;
        }

        sender.Cursor = Cursors.Default;
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
            sender.Cursor = Cursors.Default;
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

    private void DrawWireGrip(Graphics graphics, RectangleF bounds)
    {
        using var fill = new SolidBrush(Color.White);
        using var border = new Pen(_outlineColor, 2f);
        graphics.FillEllipse(fill, bounds);
        graphics.DrawEllipse(border, bounds);
    }

    private void DrawTitle(Graphics graphics, RectangleF bounds)
    {
        using var path = TopRoundedRect(bounds, CornerRadius);

        var topPt = new PointF(bounds.Left, bounds.Top);
        var botPt = new PointF(bounds.Left, bounds.Bottom);
        var topColor = Color.FromArgb(255, 232, 188, 255);
        var botColor = Color.FromArgb(255, 245, 234, 250);

        using var fill = new LinearGradientBrush(topPt, botPt, topColor, botColor);
        using var border = new Pen(_outlineColor, 1f);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        // drawing the little hilights
        bounds.Inflate(-1f, -1f);
        using var shinePath = TopRoundedRect(bounds, CornerRadius - 1);
        var shineGradient = new LinearGradientBrush(topPt, botPt, Color.White, Color.FromArgb(100, 255, 255, 255));
        using var shineBorder = new Pen(shineGradient, 1f);
        graphics.DrawPath(shineBorder, shinePath);

        // draw the actual component nickname
        using var txtBrush = new SolidBrush(_outlineColor);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(Owner.NickName, GH_FontServer.StandardAdjusted, txtBrush, bounds, fmt);
    }

    private void DrawConvo(Graphics graphics, RectangleF bounds)
    {
        var textArea = bounds; // capture before inflate modifies the local copy

        using var path = new GraphicsPath();
        path.AddRectangle(bounds);
        using var fill = new SolidBrush(Color.FromArgb(255, 245, 234, 250));
        using var border = new Pen(_outlineColor, 1f);
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

        // draw conversation messages bottom-to-top (newest at bottom, oldest scroll off the top)
        var messages = _prompt.Conversation.Messages;
        if (messages.Count == 0)
            return;

        const float padding = 6f;
        float lineHeight = GH_FontServer.ConsoleSmallAdjusted.GetHeight(graphics) + 3f;
        float x = textArea.X + padding;
        float width = textArea.Width - padding * 2f - 14f; // inset from wire grip on right edge
        float y = textArea.Bottom - padding - lineHeight;

        using var userBrush = new SolidBrush(_outlineColor);
        using var assistantBrush = new SolidBrush(Color.FromArgb(160, 47, 8, 87));
        using var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (y < textArea.Top + padding)
                break;

            var msg = messages[i];
            bool isUser = msg.Role == "user";
            string display = isUser ? $"you: {msg.Content}" : "ai: [script]";

            graphics.DrawString(
                display,
                GH_FontServer.ConsoleSmallAdjusted,
                isUser ? userBrush : assistantBrush,
                new RectangleF(x, y, width, lineHeight),
                fmt);

            y -= lineHeight;
        }
    }

    private void DrawInput(Graphics graphics, RectangleF bounds)
    {
        using var path = BottomRoundedRect(bounds, CornerRadius);
        using var fill = new SolidBrush(Color.FromArgb(255, 245, 234, 250));
        using var border = new Pen(_outlineColor, 1f);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        // draw text when user isn't currently inputting
        if (!inputPromptCurrent)
        {
            using var txtBrush = new SolidBrush(_outlineColor);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString(_prompt.UserPromptText, GH_FontServer.ConsoleSmallAdjusted, txtBrush, bounds, fmt);
        }
    }

    private void DrawResizeGrip(Graphics graphics, RectangleF grip)
    {
        using var pen = new Pen(Color.FromArgb(140, 14, 40, 240), 1f);

        grip.Inflate(-2f, -2f);
        graphics.DrawEllipse(pen, grip);
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
