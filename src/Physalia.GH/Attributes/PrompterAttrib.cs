// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Physalia.Core.ConvoInstruct;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes for the Prompter component. A panel-inspired chat interface divided
/// into three vertically stacked sections: title, conversation and entry. The conversation
/// section displays the active conversation of the Recorder wired to the Prompt Signal
/// output; the entry section opens an in-place TextBox on double-click and submits on
/// Shift+Enter. Ported from the original main-branch Composer UI.
/// </summary>
public class PrompterAttrib : GH_ComponentAttributes
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
    private readonly Color _titleColor = Color.White;
    private readonly Color _convoColor = Color.FromArgb(255, 218, 243, 245);
    private readonly Color _inputColor = Color.FromArgb(255, 138, 194, 207);
    private readonly Color _inputLoLight = Color.FromArgb(255, 36, 84, 107);

    private readonly Color _userMsgColor = Color.FromArgb(255, 119, 0, 255);
    private readonly Color _llmMsgColor = Color.FromArgb(255, 0, 34, 255);

    private readonly System.Windows.Forms.Timer _animTimer; // used for the little status message animations

    // FIELDS =======================================================================================
    private readonly Prompter _prompter; // the prompter component
    private bool _inputPromptCurrent; // is the user currently inputting text?
    private bool _submitting; // true while a Shift+Enter submit is in flight

    // sizing state — persisted across saves
    private float _width;
    private float _convoHeight; // history section
    private float _inputHeight; // entry section

    // computed section rectangles — rebuilt in Layout()
    private RectangleF _boundsTitle;
    private RectangleF _boundsConvoPanel; // the panel that holds the conversation + scrollbar
    private RectangleF _boundsConvo; // the bounds of only the conversation itself
    private RectangleF _boundsInput;
    private RectangleF _gripConvo;
    private RectangleF _gripInput;
    private RectangleF _wireOutputGrip;

    private RectangleF _layoutBounds; // the actual bounds, expanded for the output wire grip
    private RectangleF _renderBounds; // the bounds that are rendered

    // resize drag state
    private ResizeTarget _activeGrip;
    private PointF _resizeStart;
    private float _widthAtStart;
    private float _convoHeightAtStart;
    private float _inputHeightAtStart;

    // convo scroll state
    private float _scrollOffset;
    private float _scrollOffsetAtDragStart;
    private RectangleF _scrollbarTrack;
    private RectangleF _scrollbarThumb;

    // message display cache — Conversation is immutable and replaced on every append, so
    // the reference itself is the cheapest possible change signature (count alone would
    // miss MergeIntoLastUserMessage, which grows a message without changing the count)
    private string[] _cachedMsgTexts = Array.Empty<string>();
    private bool[] _cachedMsgIsUser = Array.Empty<bool>();
    private float[] _cachedMsgHeights = Array.Empty<float>();
    private float _totalContentHeight;
    private Conversation? _cachedConversation;
    private float _cachedMeasureWidth;

    // the wired Recorder, the input panel message and whether prompting is allowed —
    // refreshed every render pass (wire connections don't trigger a re-solve)
    private Recorder? _recorder;
    private string _inputMsg = string.Empty;
    private bool _canInput;

    // current animation frame
    private int _animFrame;

    // CONSTRUCTOR =======================================================================================

    /// <summary>
    /// Initializes a new instance of the <see cref="PrompterAttrib"/> class.
    /// </summary>
    /// <param name="prompter">The Prompter component that owns these attributes.</param>
    public PrompterAttrib(Prompter prompter)
        : base(prompter)
    {
        _prompter = prompter;

        // used for the animation while the pipeline is busy
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
    /// The draft prompt text persists on the component itself.
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
        double w = DefaultWidth, convoH = DefaultConvoHeight, inputH = DefaultInputHeight;
        reader.TryGetDouble("Width", ref w);
        reader.TryGetDouble("ConvoHeight", ref convoH);
        reader.TryGetDouble("InputHeight", ref inputH);
        _width = (float)w;
        _convoHeight = (float)convoH;
        _inputHeight = (float)inputH;
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
        float convoWidth = _boundsConvoPanel.Width - (ConvoPadding * 3f) - ScrollbarWidth - GripSize; // a bit of extra space on the side
        float convoHeight = _boundsConvoPanel.Height - (ConvoPadding * 2f);
        _boundsConvo = new RectangleF(convoX, convoY, convoWidth, convoHeight);

        _boundsInput = new RectangleF(x, y + TitleHeight + _convoHeight, _width, _inputHeight);

        // render bounds is the smaller rectangle (the visible panel); layout bounds is
        // slightly bigger to the right so the output grip stays clickable
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
        Bounds = _renderBounds; // set the bounds back to the visible panel for the render pass

        // grab the connected Recorder in the render pass — connecting an output wire
        // doesn't trigger a re-solve, so this is the only reliable refresh point
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

        Bounds = _layoutBounds; // reset to layout bounds so the output grip stays clickable
    }

    // EVENT HANDLERS ===================================================================================

    /// <summary>
    /// Opens an in-place TextBox overlay over the input section on double-click.
    /// Shift+Enter submits the text as a Prompt Signal; clicking away keeps the draft.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if the input section was double-clicked; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_boundsInput.Contains(e.CanvasLocation) && _canInput)
        {
            var inputBox = DrawInputTextBox(sender);
            _inputPromptCurrent = true;
            sender.Controls.Add(inputBox);
            inputBox.BringToFront();
            inputBox.Focus();
            inputBox.SelectAll();

            inputBox.Leave += (s, _) =>
            {
                if (!_submitting)
                {
                    // user clicked away without submitting — just persist the draft text
                    _prompter.UserPromptText = inputBox.Text;
                    _prompter.ExpireSolution(true);
                }

                _submitting = false;
                _inputPromptCurrent = false;
                sender.Controls.Remove(inputBox);
            };

            // Shift+Enter submits the prompt as a signal
            inputBox.KeyDown += (s, keyArgs) =>
            {
                if (keyArgs.KeyCode == Keys.Enter && keyArgs.Shift)
                {
                    keyArgs.SuppressKeyPress = true; // prevent the newline being added
                    _prompter.UserPromptText = inputBox.Text;
                    _submitting = true;
                    _prompter.SubmitUserMessage(); // mints the signal, clears the draft, expires
                    sender.Focus(); // triggers Leave, which removes the TextBox
                }
            };

            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseDoubleClick(sender, e);
    }

    /// <summary>
    /// Captures a resize or scroll drag when the user presses the mouse button inside a grip.
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
    /// Updates section sizes during a resize drag; shows the resize cursor when hovering a grip.
    /// Horizontal drag always resizes the shared width; vertical drag resizes only the dragged section.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if a drag is in progress; otherwise the base response.</returns>
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
                    // dragging thumb up (dy < 0) increases scroll offset (reveals older messages)
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
    /// Ends a resize or scroll drag.
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
        using var shineGradient = new LinearGradientBrush(topPt, botPt, _titleHilightColor, Color.FromArgb(100, 255, 255, 255));
        using var shineBorder = new Pen(shineGradient, 1f);
        graphics.DrawPath(shineBorder, shinePath);

        // draw the actual component nickname
        using var txtBrush = new SolidBrush(_outlineColor);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(Owner.NickName, GH_FontServer.StandardAdjusted, txtBrush, bounds, fmt);
    }

    // draws the conversation panel background and outline
    private void DrawConvoPanel(Graphics graphics, RectangleF bounds)
    {
        // the main convo rectangle
        using var fill = new SolidBrush(_convoColor);
        graphics.FillRectangle(fill, bounds);

        // the outline — we don't want a line between convo and input
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

        using var shineGradient = new LinearGradientBrush(bounds, Color.FromArgb(200, 255, 255, 255), _inputColor, LinearGradientMode.Vertical);
        shineGradient.WrapMode = WrapMode.TileFlipXY;
        using var shineBorder = new Pen(shineGradient, 1f);
        graphics.DrawPath(shineBorder, hilightPath);
    }

    // draw conversation messages bottom-to-top (newest at bottom, oldest scroll off the top)
    private void DrawConvoText(Graphics graphics)
    {
        using var fmt = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.Word,
        };

        RebuildMessageCache(graphics, _recorder?.ActiveConversation, fmt);

        if (_cachedMsgTexts.Length == 0)
        {
            return;
        }

        using var userBrush = new SolidBrush(_userMsgColor); // user text colour
        using var assistantBrush = new SolidBrush(_llmMsgColor); // llm text colour

        float maxScroll = Math.Max(0f, _totalContentHeight - _boundsConvo.Height);
        _scrollOffset = Math.Clamp(_scrollOffset, 0f, maxScroll);

        DrawScrollbar(graphics, _scrollbarTrack, maxScroll);

        // clip drawing to the text area so partially-visible messages are trimmed at the edges
        var state = graphics.Save();
        graphics.SetClip(_boundsConvo);

        float msgYPos = _boundsConvo.Bottom + _scrollOffset;
        for (int i = _cachedMsgTexts.Length - 1; i >= 0; i--)
        {
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

            // clip region handles any partial visibility at top or bottom
            graphics.DrawString(
                _cachedMsgTexts[i],
                _convoFont,
                _cachedMsgIsUser[i] ? userBrush : assistantBrush,
                new RectangleF(_boundsConvo.X, msgYPos, _boundsConvo.Width, msgHeight),
                fmt);
        }

        graphics.Restore(state);
    }

    // rebuilds the display strings + measured heights when the conversation reference or
    // the panel width changes; Conversation is immutable, so the reference IS the signature
    private void RebuildMessageCache(Graphics graphics, Conversation? conversation, StringFormat fmt)
    {
        bool sameConversation = ReferenceEquals(conversation, _cachedConversation);
        bool sameWidth = (int)_boundsConvo.Width == (int)_cachedMeasureWidth;
        if (sameConversation && sameWidth)
        {
            return;
        }

        if (conversation is null || conversation.Count == 0)
        {
            _cachedMsgTexts = Array.Empty<string>();
            _cachedMsgIsUser = Array.Empty<bool>();
            _cachedMsgHeights = Array.Empty<float>();
            _totalContentHeight = 0f;
            _cachedConversation = conversation;
            _cachedMeasureWidth = _boundsConvo.Width;
            return;
        }

        int count = conversation.Count;
        _cachedMsgTexts = new string[count];
        _cachedMsgIsUser = new bool[count];
        _cachedMsgHeights = new float[count];
        float totalMsgHeight = 0f;

        for (int i = 0; i < count; i++)
        {
            ConversationMessage message = conversation.Messages[i];
            _cachedMsgTexts[i] = FormatMessage(message);
            _cachedMsgIsUser[i] = message.Role == Role.User;
            _cachedMsgHeights[i] = graphics.MeasureString(_cachedMsgTexts[i], _convoFont, (int)_boundsConvo.Width, fmt).Height;
            totalMsgHeight += _cachedMsgHeights[i];
        }

        _totalContentHeight = totalMsgHeight;
        _cachedConversation = conversation;
        _cachedMeasureWidth = _boundsConvo.Width;
    }

    // flattens a message's content blocks into one display string
    private static string FormatMessage(ConversationMessage message)
    {
        var parts = new List<string>(message.Content.Count);
        foreach (MessageContent block in message.Content)
        {
            switch (block)
            {
                case TextContent text:
                    parts.Add(text.Text);
                    break;
                case ImageContent:
                    parts.Add("[image]");
                    break;
            }
        }

        return string.Join(Environment.NewLine, parts);
    }

    // the input panel background
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

    // draw the text when the user isn't actively inputting:
    // the draft prompt if one exists, otherwise the status message.
    private void DrawInputText(Graphics graphics, RectangleF bounds)
    {
        using var txtBrush = new SolidBrush(Color.White);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        var inputMsg = (!_inputPromptCurrent && _prompter.UserPromptText != string.Empty) ? _prompter.UserPromptText : _inputMsg;
        graphics.DrawString(inputMsg, GH_FontServer.ConsoleAdjusted, txtBrush, bounds, fmt);
    }

    // the textbox for active input
    private TextBox DrawInputTextBox(GH_Canvas sender)
    {
        float zoom = sender.Viewport.Zoom;
        PointF origin = sender.Viewport.ProjectPoint(_boundsInput.Location);

        var tb = new TextBox
        {
            Multiline = true,
            WordWrap = true,
            ScrollBars = ScrollBars.None,
            BorderStyle = BorderStyle.None,
            BackColor = _inputColor,
            Font = GH_FontServer.Console,
            Text = _prompter.UserPromptText,
            Bounds = new Rectangle((int)origin.X + 10, (int)origin.Y + 60, (int)((_boundsInput.Width * zoom) - 20), (int)((_boundsInput.Height * zoom) - 108)),
        };
        return tb;
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
        float thumbY = track.Y + (thumbFrac * (track.Height - thumbH));
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

    // little helper function to get four points of a rectangle
    private static Dictionary<string, PointF> GetRectCornerPts(RectangleF rect)
    {
        var ptDict = new Dictionary<string, PointF>
        {
            { "topLeft", new PointF(rect.Left, rect.Top) },
            { "topRight", new PointF(rect.Right, rect.Top) },
            { "botLeft", new PointF(rect.Left, rect.Bottom) },
            { "botRight", new PointF(rect.Right, rect.Bottom) },
        };

        return ptDict;
    }

    // finds the Recorder wired to the Prompt Signal output, decides the input panel's
    // status message and whether the prompt textbox may open. Called every render pass —
    // connecting an output wire doesn't trigger a re-solve.
    private (string Msg, bool CanPrompt) GetPromptInfo()
    {
        _recorder = FindRecorder();
        bool busy = _recorder is not null && IsPipelineBusy(_recorder);

        // start or stop the busy animation as needed
        SetAnimationFrame(busy);

        if (_recorder is null)
        {
            return ("Connect a Recorder to begin.", false);
        }

        if (busy)
        {
            var ani = GetAnimation(_animFrame, "wave");
            return ($"{ani} Working {ani}", false);
        }

        // good to prompt!
        return ("Double click to prompt.", true);
    }

    // walks the Prompt Signal output's recipients looking for a Recorder
    private Recorder? FindRecorder()
    {
        foreach (var recipient in _prompter.Params.Output[0].Recipients)
        {
            if (recipient.Attributes?.GetTopLevel?.DocObject is Recorder recorder)
            {
                return recorder;
            }
        }

        return null;
    }

    // busy while the Recorder itself is mid-run, or while any lifecycle component
    // consuming the Recorder's outgoing Signal (i.e. the Reasoner) is mid-run
    private static bool IsPipelineBusy(Recorder recorder)
    {
        if (recorder.IsBusy)
        {
            return true;
        }

        foreach (var recipient in recorder.Params.Output[1].Recipients)
        {
            if (recipient.Attributes?.GetTopLevel?.DocObject is StatefulComponentBase stateful && stateful.IsBusy)
            {
                return true;
            }
        }

        return false;
    }

    // returns an ascii animation frame
    private static string GetAnimation(int time, string animation)
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

    private void SetAnimationFrame(bool busy)
    {
        if (busy)
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
