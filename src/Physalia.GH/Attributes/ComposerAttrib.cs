// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Physalia.Core.Providers;
using Physalia.GH.Attributes.UiComponents;
using Physalia.GH.Components;
using Physalia.GH.ParamTypes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the COMPOSER component. A panel-inspired prompting interface
/// divided into three vertically stacked sections: title, conversation and entry.
/// </summary>
public class ComposerAttrib : GH_ComponentAttributes
{
    // NESTED TYPES =======================================================================================

    // identifies which resize grip is currently being dragged
    private enum ResizeTarget
    {
        None,
        ConvoTarget,
        InputTarget,
        ScrollThumb,
    }

    // CONSTANTS =======================================================================================
    private const float TitleHeight = 18f;
    private const float GripSize = 8f;
    private const float CornerRadius = 4f;
    private const float MinWidth = 140f;
    private const float MinSectionHeight = 40f;
    private const float DefaultWidth = 220f;
    private const float DefaultConvoHeight = 120f;
    private const float DefaultInputHeight = 80f;
    private const float ConvoPadding = 6f; // room between conversation and convo panel
    private const float ScrollbarWidth = 10f;

    private readonly Font _convoFont = GH_FontServer.ConsoleSmallAdjusted;

    private readonly Color _outlineColor = Color.FromArgb(255, 47, 8, 87);
    private readonly Color _titleHilightColor = Color.FromArgb(255, 232, 188, 255);
    private readonly Color _titleColor = Color.White; // formely 255, 232, 188, 255
    private readonly Color _convoColor = Color.FromArgb(255, 218, 243, 245); // formerly 255, 245, 234, 250
    private readonly Color _inputColor = Color.FromArgb(255, 138, 194, 207);
    private readonly Color _inputLoLight = Color.FromArgb(255, 36, 84, 107);

    private readonly Color _userMsgColor = Color.FromArgb(255, 119, 0, 255);
    private readonly Color _llmMsgColor = Color.FromArgb(255, 0, 34, 255);

    private readonly System.Windows.Forms.Timer _animTimer; // used for the little status message animations

    // FIELDS =======================================================================================
    private readonly Composer _composerComponent; // the composer component
    private bool inputPromptCurrent = false; // is the user currently inputing text?
    private bool _submitting = false; // true while a Shift+Enter submit is in flight

    // sizing state — persisted across saves
    private float _width;
    private float _convoHeight; // history section
    private float _inputHeight; // entry section

    // computed section rectangles — rebuilt in Layout()
    private RectangleF _boundsTitle;
    private RectangleF _boundsConvoPanel; // the panel that holders the conversation + scrollbar
    private RectangleF _boundsConvo; // the bounds of only the conversation itself
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

    // convo scroll state
    private float _scrollOffset = 0f;
    private float _scrollOffsetAtDragStart;
    private RectangleF _scrollbarTrack;
    private RectangleF _scrollbarThumb;

    // message height cache — recomputed only when count or width changes
    private float[] _cachedMsgHeights = Array.Empty<float>();
    private float _totalContentHeight;
    private int _cachedMsgCount = -1;
    private float _cachedMeasureWidth;

    // input message and can input text bool - comes from connected transmitter component
    private string _inputMsg = string.Empty;
    private bool _canInput = false;

    // current animation frame
    private int _animFrame = 0;

    // CONSTRUCTOR =======================================================================================

    /// <summary>
    /// Initializes a new instance of the <see cref="ComposerAttrib"/> class.
    /// </summary>
    /// <param name="composer">The COMPOSER component that owns these attributes.</param>
    public ComposerAttrib(Composer composer)
        : base(composer)
    {
        _composerComponent = composer;

        // used for the animations when api status is shown in input
        _animTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _animTimer.Tick += (s, e) =>
        {
            _animFrame++;
            Grasshopper.Instances.ActiveCanvas?.Refresh();
        };
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
        writer.SetString("PromptText", _composerComponent.UserPromptText ?? string.Empty);
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
        {
            _composerComponent.UserPromptText = promptText;
        }

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

        _boundsConvoPanel = new RectangleF(x, y + TitleHeight, _width, _convoHeight);

        float convoX = _boundsConvoPanel.X + ConvoPadding;
        float convoY = _boundsConvoPanel.Y + ConvoPadding;
        float convoWidth = _boundsConvoPanel.Width - (ConvoPadding * 3f) - ScrollbarWidth - GripSize; // added a bit extra space on side.. convoPadding * 3
        float convoHeight = _boundsConvoPanel.Height - (ConvoPadding * 2f);
        _boundsConvo = new RectangleF(convoX, convoY, convoWidth, convoHeight);

        _boundsInput = new RectangleF(x, y + TitleHeight + _convoHeight, _width, _inputHeight);

        // use the renderBounds / layoutBounds so we can make sure our output wire is actually clickable
        // render bounds is the smaller bounds (the main component), layout bounds is slightly bigger to the right for output grip
        _renderBounds = new RectangleF(x, y, _width, TitleHeight + _convoHeight + _inputHeight);
        _layoutBounds = new RectangleF(x, y, _width + 4f, TitleHeight + _convoHeight + _inputHeight);

        Bounds = _renderBounds;

        _gripConvo = new RectangleF(_boundsConvoPanel.Right - GripSize - 1f, _boundsConvoPanel.Bottom - GripSize, GripSize, GripSize);
        _gripInput = new RectangleF(_boundsInput.Right - GripSize - 1f, _boundsInput.Bottom - GripSize - 1f, GripSize, GripSize);

        _scrollbarTrack = new RectangleF(_boundsConvoPanel.Right - GripSize + 0.5f, _boundsConvoPanel.Top + 2.5f, GripSize - 3f, _boundsConvoPanel.Height - GripSize - 3f);

        _wireOutputGrip = new RectangleF(_boundsConvoPanel.Right - 3f, _boundsConvoPanel.Bottom - (_boundsConvoPanel.Height / 2) - 4f, 8f, 8f);

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

        // have to grab the connected component in the render pass since solveinstance isn't triggered on output wire connection
        // TODO - FIGURE OUT IF THERE IS A CHEAPER WAY TO DO THIS, or maybe this isn't super expensive. LOOK INTO IT!
        (_inputMsg, _canInput) = GetPromptInfo();

        if (channel == GH_CanvasChannel.Objects)
        {
            DrawWireGrip(graphics, _wireOutputGrip);
            DrawTitle(graphics, _boundsTitle);
            DrawConvoPanel(graphics, _boundsConvoPanel);
            DrawConvoText(graphics);
            DrawInputPanel(graphics, _boundsInput);
            DrawInputText(graphics, _boundsInput);
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
        // creating the input box
        if (_boundsInput.Contains(e.CanvasLocation) && _canInput)
        {
            var inputBox = DrawInputTextBox(sender);
            inputPromptCurrent = true;

            // user clicked away — persist the draft and close
            inputBox.Popup.Deactivate += (s, _) =>
            {
                if (_submitting)
                    return; // Shift+Enter path handles everything; avoid double-fire

                _composerComponent.UserPromptText = inputBox.Text;
                _composerComponent.ExpireSolution(true);
                inputPromptCurrent = false;
                inputBox.Close();
            };

            // cleanup flags once the popup is fully gone (covers both exit paths)
            inputBox.Popup.FormClosed += (s, _) =>
            {
                _submitting = false;
                inputPromptCurrent = false;
            };

            // shift + enter submits the message to the conversation history
            inputBox.RichTextBox.KeyDown += (s, keyArgs) =>
            {
                if (keyArgs.KeyCode == Keys.Enter && keyArgs.Shift)
                {
                    keyArgs.SuppressKeyPress = true; // prevent the newline being added
                    _composerComponent.UserPromptText = inputBox.Text;
                    _submitting = true;
                    _composerComponent.SubmitUserMessage(); // appends to history, clears UserPromptText, expires solution
                    inputBox.Close(); // programmatic close does not fire Deactivate
                }
            };

            inputBox.Show(sender.FindForm()); // owned by GH editor; stays on top, disappears on deactivate
            inputBox.SelectAll();
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
            if (_scrollbarThumb.Contains(e.CanvasLocation))
            {
                _activeGrip = ResizeTarget.ScrollThumb;
                _resizeStart = e.CanvasLocation;
                _scrollOffsetAtDragStart = _scrollOffset;
                return GH_ObjectResponse.Capture;
            }

            if (_scrollbarTrack.Contains(e.CanvasLocation))
            {
                // click on track (not thumb) — jump scroll to that position
                float clickFrac = (e.CanvasLocation.Y - _scrollbarTrack.Y) / _scrollbarTrack.Height;
                float maxScroll = Math.Max(0f, _totalContentHeight - (_boundsConvoPanel.Height - (ConvoPadding * 2f)));
                _scrollOffset = Math.Clamp(maxScroll * (1f - clickFrac), 0f, maxScroll);
                ExpireLayout();
                sender.ScheduleRegen(2);
                return GH_ObjectResponse.Handled;
            }

            if (_gripConvo.Contains(e.CanvasLocation))
            {
                _activeGrip = ResizeTarget.ConvoTarget;
                _resizeStart = e.CanvasLocation;
                _widthAtStart = _width;
                _convoHeightAtStart = _convoHeight;
                return GH_ObjectResponse.Capture;
            }

            if (_gripInput.Contains(e.CanvasLocation))
            {
                _activeGrip = ResizeTarget.InputTarget;
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

            if (_activeGrip == ResizeTarget.ScrollThumb)
            {
                float maxScroll = Math.Max(0f, _totalContentHeight - (_boundsConvoPanel.Height - (ConvoPadding * 2f)));
                float trackRange = _scrollbarTrack.Height - _scrollbarThumb.Height;
                if (trackRange > 0f)
                {
                    // dragging thumb up (dy < 0) increases scroll offset (reveals older displayMessages)
                    _scrollOffset = Math.Clamp(_scrollOffsetAtDragStart - (dy * maxScroll / trackRange), 0f, maxScroll);
                }

                sender.Cursor = Cursors.SizeNS;
                ExpireLayout();
                sender.ScheduleRegen(2);
                return GH_ObjectResponse.Handled;
            }

            _width = Math.Max(MinWidth, _widthAtStart + dx);

            if (_activeGrip == ResizeTarget.ConvoTarget)
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

    // positions the output param at the right edge of the conversation section, vertically centred
    private void LayoutOutputParam()
    {
        var param = Owner.Params.Output[0];
        float midY = _boundsConvoPanel.Y + (_boundsConvoPanel.Height / 2f);
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

        using var fill = new LinearGradientBrush(topPt, botPt, _titleColor, _convoColor);
        using var border = new Pen(_outlineColor, 1f);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        // drawing the little hilights
        bounds.Inflate(-1f, -1f);
        using var shinePath = TopRoundedRect(bounds, CornerRadius - 1);
        var shineGradient = new LinearGradientBrush(topPt, botPt, _titleHilightColor, Color.FromArgb(100, 255, 255, 255));
        using var shineBorder = new Pen(shineGradient, 1f);
        graphics.DrawPath(shineBorder, shinePath);

        // draw the actual component nickname
        using var txtBrush = new SolidBrush(_outlineColor);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(Owner.NickName, GH_FontServer.StandardAdjusted, txtBrush, bounds, fmt);
    }

    // draws the conversation in the convo bounds
    private void DrawConvoPanel(Graphics graphics, RectangleF bounds)
    {
        // the main convo rectangle
        using var fill = new SolidBrush(_convoColor);
        graphics.FillRectangle(fill, bounds);

        // the outline - we don't want a line between convo and input
        using var path = new GraphicsPath();
        using var border = new Pen(_outlineColor, 1f);

        var rp = GetRectCornerPts(bounds); // the rectangle corners as a dict
        path.AddLine(rp["botLeft"], rp["topLeft"]);
        path.AddLine(rp["topLeft"], rp["topRight"]);
        path.AddLine(rp["topRight"], rp["botRight"]);
        graphics.DrawPath(border, path);

        // drawing the little hilights
        bounds.Inflate(-1f, -1f);
        using var hilightPath = new GraphicsPath();
        var hp = GetRectCornerPts(bounds);
        PointF[] pathPts = { hp["botLeft"], hp["topLeft"], hp["topRight"], hp["botRight"] };

        hilightPath.AddLines(pathPts);

        var shineGradient = new LinearGradientBrush(bounds, Color.FromArgb(200, 255, 255, 255), _inputColor, LinearGradientMode.Vertical);
        shineGradient.WrapMode = WrapMode.TileFlipXY;
        using var shineBorder = new Pen(shineGradient, 1f);
        graphics.DrawPath(shineBorder, hilightPath);
    }

    // draw conversation displayMessages bottom-to-top (newest at bottom, oldest scroll off the top)
    private void DrawConvoText(Graphics graphics)
    {
        var displayMessages = _composerComponent.Conversation.HumanMessages;
        var llmMessages = _composerComponent.Conversation.LlmMessages;
        if (displayMessages.Count == 0)
        {
            return;
        }

        using var userBrush = new SolidBrush(_userMsgColor); // user text color
        using var assistantBrush = new SolidBrush(_llmMsgColor); // llm text color

        using var fmt = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.Word,
        };

        // recompute message heights only when count or panel width changes
        if (displayMessages.Count != _cachedMsgCount || (int)_boundsConvo.Width != (int)_cachedMeasureWidth)
        {
            _cachedMsgHeights = new float[displayMessages.Count];
            float totalMsgHeight = 0f; // total pixel height of messages

            for (int i = 0; i < displayMessages.Count; i++)
            {
                var displayMsg = displayMessages[i];
                _cachedMsgHeights[i] = graphics.MeasureString(displayMsg, _convoFont, (int)_boundsConvo.Width, fmt).Height;
                totalMsgHeight += _cachedMsgHeights[i];
            }

            _totalContentHeight = totalMsgHeight;
            _cachedMsgCount = displayMessages.Count;
            _cachedMeasureWidth = _boundsConvo.Width;
        }

        float maxScroll = Math.Max(0f, _totalContentHeight - _boundsConvo.Height);
        _scrollOffset = Math.Clamp(_scrollOffset, 0f, maxScroll);

        DrawScrollbar(graphics, _scrollbarTrack, maxScroll);

        // clip drawing to the text area so partially-visible displayMessages are trimmed at the edges
        var state = graphics.Save();
        graphics.SetClip(_boundsConvo);

        float msgYPos = _boundsConvo.Bottom + _scrollOffset;
        for (int i = displayMessages.Count - 1; i >= 0; i--)
        {
            var displayMsg = displayMessages[i];

            float msgHeight = _cachedMsgHeights[i];
            msgYPos -= msgHeight;

            // completely above the viewport — nothing older will be visible either
            if (msgYPos + msgHeight < _boundsConvo.Top)
            {
                break;
            }

            // completely below the viewport — skip but keep iterating upward
            if (msgYPos > _boundsConvo.Bottom)
            {
                continue;
            }

            // figuring out if the message is llm or user
            var role = llmMessages[i].Role;
            var isUser = role == "user";

            // clip region handles any partial visibility at top or bottom
            graphics.DrawString(
                displayMsg,
                _convoFont,
                isUser ? userBrush : assistantBrush,
                new RectangleF(_boundsConvo.X, msgYPos, _boundsConvo.Width, msgHeight),
                fmt);
        }

        graphics.Restore(state);
    }

    // the input box when the user is currently inputting text
    private void DrawInputPanel(Graphics graphics, RectangleF bounds)
    {
        using var fillPath = BottomRoundedRect(bounds, CornerRadius);
        using var fill = new LinearGradientBrush(bounds, _convoColor, _inputColor, LinearGradientMode.Vertical);
        fill.WrapMode = WrapMode.TileFlipXY;
        var blend = new ColorBlend(3);
        blend.Colors = new[] { _convoColor, _inputColor, _inputColor };
        blend.Positions = new[] { 0f, 0.125f, 1f };
        fill.InterpolationColors = blend;
        graphics.FillPath(fill, fillPath);

        // border doesn't have a top line
        using var outlinePath = BottomRoundedRectOpenTop(bounds, CornerRadius);
        using var border = new Pen(_outlineColor, 1f);
        graphics.DrawPath(border, outlinePath);

        // draw the little lolight
        var hilightRect = new RectangleF(bounds.Left + 1f, bounds.Top - 4f, bounds.Width - 2f, bounds.Height + 3f);
        using var hilightPath = BottomRoundedRectOpenTop(hilightRect, CornerRadius - 1f);
        using var hilightGrad = new LinearGradientBrush(hilightRect, _inputColor, _inputLoLight, LinearGradientMode.Vertical);
        hilightGrad.WrapMode = WrapMode.TileFlipXY;
        using var hilightBorder = new Pen(hilightGrad, 1f);
        graphics.DrawPath(hilightBorder, hilightPath);
    }

    // draw the text when the user isn't actively inputting.
    // will be default message if no WIP prompt text exists.
    // will be animation if COMPOSER is WIP.
    private void DrawInputText(Graphics graphics, RectangleF bounds)
    {
        using var txtBrush = new SolidBrush(Color.White);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        // set input msg based on transmitter status, unless user started entering prompt and clicked away.
        var inputMsg = (!inputPromptCurrent && _composerComponent.UserPromptText != string.Empty) ? _composerComponent.UserPromptText : _inputMsg;
        graphics.DrawString(inputMsg, GH_FontServer.ConsoleSmallAdjusted, txtBrush, bounds, fmt);
    }

    // the textbox for active input — shown as a floating popup over the canvas
    private EtoRichTextBox DrawInputTextBox(GH_Canvas sender)
    {
        // Inset the popup slightly inside the drawn input panel bounds.
        const float padding = 4f;
        var inputBounds = new RectangleF(
            _boundsInput.X + padding,
            _boundsInput.Y + padding,
            _boundsInput.Width - (padding * 2f),
            _boundsInput.Height - (padding * 2f));

        // ConsoleSmallAdjusted is already scaled by the current viewport zoom, so the popup
        // text matches the apparent size of canvas-drawn text at any zoom level.
        return new EtoRichTextBox(sender, inputBounds, _inputColor, GH_FontServer.Console, _composerComponent.UserPromptText);
    }

    private void DrawResizeGrip(Graphics graphics, RectangleF grip)
    {
        grip.Inflate(-2f, -2f);
        using var hilight = new LinearGradientBrush(grip, Color.FromArgb(0, 255, 255, 255), Color.White, LinearGradientMode.Vertical);
        hilight.WrapMode = WrapMode.TileFlipXY;
        using var shadow = new LinearGradientBrush(grip, _inputLoLight, Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Vertical);
        using var pen = new Pen(hilight, 0.5f);

        graphics.DrawEllipse(pen, grip);
        grip.Inflate(-0.5f, -0.5f);
        graphics.FillEllipse(shadow, grip);
    }

    private void DrawScrollbar(Graphics graphics, RectangleF track, float maxScroll)
    {
        // track background
        using var trackBrush = new SolidBrush(Color.FromArgb(40, 47, 8, 87));
        graphics.FillRectangle(trackBrush, track);

        if (maxScroll <= 0f)
        {
            return;
        }

        // thumb — height proportional to visible fraction, position reflects scroll offset
        float thumbH = Math.Max(20f, track.Height * (track.Height / _totalContentHeight));
        float thumbFrac = 1f - (_scrollOffset / maxScroll); // 0 = top, 1 = bottom
        float thumbY = track.Y + thumbFrac * (track.Height - thumbH);
        _scrollbarThumb = new RectangleF(track.X, thumbY, track.Width, thumbH);

        using var thumbBrush = new SolidBrush(Color.FromArgb(120, 47, 8, 87));
        graphics.FillRectangle(thumbBrush, _scrollbarThumb);
    }

    private static GraphicsPath TopRoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); // top left arc
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90); // top right arc

        var rp = GetRectCornerPts(r);
        path.AddLine(rp["botRight"], rp["botLeft"]);

        path.CloseFigure();
        return path;
    }

    private static GraphicsPath BottomRoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();

        var rp = GetRectCornerPts(r);
        path.AddLine(rp["topLeft"], rp["topRight"]);

        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath BottomRoundedRectOpenTop(RectangleF r, float radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();
        var rp = GetRectCornerPts(r);

        path.AddLine(rp["topRight"], new PointF(r.Right, r.Bottom - d)); // top right to right arc
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); // right arc
        path.AddLine(new PointF(r.Right - d, r.Bottom), new PointF(r.X + d, r.Bottom)); // between the arcs
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); // left arc
        path.AddLine(new PointF(r.X, r.Bottom - d), rp["topLeft"]); // left arc to top left

        return path;
    }

    // little helper function to get four points of rectangle
    private static Dictionary<string, PointF> GetRectCornerPts(RectangleF rect)
    {
        var ptDict = new Dictionary<string, PointF>();

        ptDict.Add("topLeft", new PointF(rect.Left, rect.Top));
        ptDict.Add("topRight", new PointF(rect.Right, rect.Top));
        ptDict.Add("botLeft", new PointF(rect.Left, rect.Bottom));
        ptDict.Add("botRight", new PointF(rect.Right, rect.Bottom));

        return ptDict;
    }

    // I need to call this within the render loop. Connecting an output wire doesn't trigger a resolve.
    // need to get the connected component to pass status methods to prompt panel
    // msg returns the msg to be displayed on the prompt input panel
    // canPrompt let's us know if the user should be able to open the prompt textbox for input
    private (string msg, bool canPrompt) GetPromptInfo()
    {
        // see if the component is actually hooked up.
        var connectedComponents = _composerComponent.Params.Output[0].Recipients;
        Transmitter transmitter = null;

        foreach (var comp in connectedComponents)
        {
            var docObj = comp.Attributes?.GetTopLevel?.DocObject;
            if (docObj is Transmitter)
            {
                transmitter = (Transmitter)docObj;
                break;
            }
        }

        // set animationFrame or stop timer if need be
        SetAnimationFrame(transmitter);

        // transmitter isn't hooked up
        if (transmitter == null)
        {
            return ("Connect a Transmitter to begin.", false);
        }

        // check if the actual inputs to the LLM input are LLM providers or LLM isn't hooked up
        if (!transmitter.LlmConnected)
        {
            return ("Connect an LLM to begin.", false);
        }

        // check if any Receiver component is hooked up
        if (transmitter.ReceiverComponent == null)
        {
            return ("Target a Receiver component to begin.", false);
        }

        // the transmitter is busy
        if (transmitter.IsBusy)
        {
            var ani = GetAnimation(_animFrame, "wave");
            return ($"{ani} {transmitter.Message} {ani}", false);
        }

        // good to prompt!
        return ("Double click to prompt.", true);
    }

    // returns an ascii animation frame
    private string GetAnimation(int time, string animation)
    {
        // add animations here
        Dictionary<string, List<string>> aniDict = new ();

        // standard waves
        var chosenAni = new List<string> { "~-~__", "_~-~_", "__~-~", "~__~-" };
        aniDict.Add("wave", chosenAni);

        if (aniDict.ContainsKey(animation))
        {
           chosenAni = aniDict[animation];
        }

        int currentFrame = (time + 1) % chosenAni.Count;
        return chosenAni[currentFrame];
    }

    private void SetAnimationFrame(Transmitter transmitter)
    {
        if (transmitter != null && transmitter.IsBusy)
        {
            if (!_animTimer.Enabled)
            {
                _animTimer.Start();
            }
        }
        else
        {
            _animTimer.Stop();
            _animFrame = 0;
        }
    }
}
