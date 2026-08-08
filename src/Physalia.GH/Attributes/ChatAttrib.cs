// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.GH.Components;

namespace Physalia.GH.Attributes;

/// <summary>
/// Attributes for the Chat component. The chat UI lives in a standalone window, opened by
/// double-clicking the node.
///
/// <para>The capsule is drawn here rather than by Grasshopper because the Chat has no inputs and is
/// not preview-capable, so the stock renderer would give it a jagged left edge and force it onto the
/// grey Hidden palette. Drawing it with both edges rounded and our own palette style sidesteps
/// both.</para>
/// </summary>
public class ChatAttrib : PhyComponentAttributes
{
    // The Chat's own tint: light-blue body, black capsule edge, dark-purple text.
    private static readonly Color ChatFill = Color.FromArgb(255, 218, 243, 245);
    private static readonly Color ChatEdge = Color.Black;
    private static readonly Color ChatText = Color.FromArgb(255, 47, 8, 87);

    private readonly Chat _chat;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatAttrib"/> class.
    /// </summary>
    /// <param name="chat">The Chat component that owns these attributes.</param>
    public ChatAttrib(Chat chat)
        : base(chat)
    {
        _chat = chat;
    }

    /// <summary>
    /// Opens the chat window on double-click.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled — the double-click is consumed to open the window.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        _chat.OpenWindow();
        Selected = false;
        sender.Refresh();
        return GH_ObjectResponse.Handled;
    }

    /// <inheritdoc/>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (channel != GH_CanvasChannel.Objects)
        {
            base.Render(canvas, graphics, channel);
            return;
        }

        RenderSmoothCapsule(canvas, graphics, new GH_PaletteStyle(ChatFill, ChatEdge, ChatText));
    }

    // Mirrors GH_ComponentAttributes.RenderComponentCapsule but rounds both edges
    // (SetJaggedEdges(false, false)) and draws the fill/edge/text from our own palette style.
    private void RenderSmoothCapsule(GH_Canvas canvas, Graphics graphics, GH_PaletteStyle style)
    {
        RectangleF rec = Bounds;
        bool visible = canvas.Viewport.IsVisible(ref rec, 10f);
        Bounds = rec;
        if (!visible)
        {
            return;
        }

        var capsule = GH_Capsule.CreateCapsule(Bounds, GH_Palette.Normal);
        try
        {
            capsule.SetJaggedEdges(false, false);

            // No inputs ever; the Prompt Signal output is the Chat's only grip.
            foreach (IGH_Param output in _chat.Params.Output)
            {
                capsule.AddOutputGrip(output.Attributes.OutputGrip.Y);
            }

            graphics.SmoothingMode = SmoothingMode.HighQuality;
            canvas.SetSmartTextRenderingHint();

            if (!string.IsNullOrWhiteSpace(_chat.Message))
            {
                capsule.RenderEngine.RenderMessage(graphics, _chat.Message, style);
            }

            capsule.Render(graphics, style);

            bool iconMode = _chat.IconDisplayMode == GH_IconDisplayMode.icon
                || (_chat.IconDisplayMode == GH_IconDisplayMode.application && Grasshopper.CentralSettings.CanvasObjectIcons);

            if (iconMode)
            {
                Image? icon = _chat.Locked ? _chat.Icon_24x24_Locked : _chat.Icon_24x24;
                if (icon != null)
                {
                    capsule.RenderEngine.RenderIcon(graphics, icon, m_innerBounds);
                }
            }
            else
            {
                var text = GH_Capsule.CreateTextCapsule(
                    m_innerBounds, m_innerBounds, GH_Palette.Black, _chat.NickName,
                    GH_FontServer.LargeAdjusted, GH_Orientation.vertical_center, 3, 6);
                text.Render(graphics, Selected, _chat.Locked, hidden: false);
                text.Dispose();
            }

            RenderComponentParameters(canvas, graphics, _chat, style);
        }
        finally
        {
            capsule.Dispose();
        }
    }
}
