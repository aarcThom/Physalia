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
/// Attributes for the Chat component. The Chat is an ordinary pipeline node: it opens nothing and
/// carries no tint of its own. The door onto the chat window is the harness proxy that holds it —
/// see <see cref="HarnessAttrib"/> — because a harness is what the user sees on their canvas.
///
/// <para>The capsule is still drawn here rather than by Grasshopper because the Chat has no inputs
/// and is not preview-capable, so the stock renderer would give it a jagged left edge and force it
/// onto the dimmed Hidden palette. Both edges are rounded and the style comes straight from
/// <see cref="GH_Skin"/>, so the node reads exactly like any other component.</para>
/// </summary>
public class ChatAttrib : PhyComponentAttributes
{
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

    // Grasshopper's own capsule style for the node's current selection and lock state. Read fresh
    // every render rather than cached, so a canvas-theme change is picked up like any other node's.
    private GH_PaletteStyle CapsuleStyle => _chat.Locked
        ? (Selected ? GH_Skin.palette_locked_selected : GH_Skin.palette_locked_standard)
        : (Selected ? GH_Skin.palette_normal_selected : GH_Skin.palette_normal_standard);

    /// <inheritdoc/>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (channel != GH_CanvasChannel.Objects)
        {
            base.Render(canvas, graphics, channel);
            return;
        }

        RenderSmoothCapsule(canvas, graphics, CapsuleStyle);
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
