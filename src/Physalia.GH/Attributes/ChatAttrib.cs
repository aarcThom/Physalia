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
    // Width (canvas units) of the strip at the node's right edge left to the output parameter, so it
    // still owns its wire grip. Everything left of it belongs to the component — see Layout.
    private const float OutputStripWidth = 6f;

    private readonly Chat _chat;

    // The bounds Grasshopper laid the output parameter out with, before Layout cut them down to the
    // grip strip. Render hands them back for the duration of the parameter draw — see RenderSmoothCapsule.
    private RectangleF _outputLayoutBounds;

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
    /// <remarks>
    /// Hands the node's width back to the component, so it can be dragged by all of itself.
    ///
    /// <para>A parameter's region belongs to the PARAMETER, not to the component, so a click there
    /// cannot drag the node. Most components get away with this because their name capsules are narrow
    /// strips down each side with the icon between them — but the Chat has no inputs and one output
    /// named "Prompt Signal", so Grasshopper's layout handed that one capsule everything the icon did
    /// not use. That left a sliver of icon as the only place the node could be picked up.</para>
    ///
    /// <para>So the output keeps only a narrow strip at the right edge — enough to own its grip, which
    /// is derived from these bounds — and the rest of the node falls to the component. The bounds
    /// Grasshopper computed are kept, and handed back while the parameter is drawn, so the node looks
    /// exactly as it always did.</para>
    /// </remarks>
    protected override void Layout()
    {
        base.Layout();

        _outputLayoutBounds = RectangleF.Empty;
        if (_chat.Params.Output.Count == 0)
        {
            return;
        }

        // base.Layout recomputes the parameter's bounds from the component box every time, so this is
        // always the full band, never a strip left over from the previous pass.
        IGH_Attributes output = _chat.Params.Output[0].Attributes;
        _outputLayoutBounds = output.Bounds;

        RectangleF node = Bounds;
        output.Bounds = new RectangleF(
            node.Right - OutputStripWidth, node.Y, OutputStripWidth, node.Height);
    }

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

            RenderOutputName(canvas, graphics, style);
        }
        finally
        {
            capsule.Dispose();
        }
    }

    // Draws the output's name capsule with Grasshopper's own renderer, which is the only way to be
    // certain it looks exactly as it did before Layout took the parameter's width away.
    //
    // RenderComponentParameters reads the parameter's bounds, and Layout has cut those down to the
    // grip strip so the component owns the rest of the node — drawing straight from them would squash
    // the name into that sliver. So the laid-out bounds are lent back for the duration of the draw and
    // returned immediately afterwards.
    //
    // Safe because painting and hit-testing never interleave: mouse events are dispatched between
    // paints, and the attribute cache holds the attribute OBJECTS rather than copies of their bounds,
    // so a click always reads the strip. The output grip's Y was taken before the swap, and is the
    // vertical centre either way.
    private void RenderOutputName(GH_Canvas canvas, Graphics graphics, GH_PaletteStyle style)
    {
        if (_outputLayoutBounds.IsEmpty || _chat.Params.Output.Count == 0)
        {
            RenderComponentParameters(canvas, graphics, _chat, style);
            return;
        }

        IGH_Attributes output = _chat.Params.Output[0].Attributes;
        RectangleF strip = output.Bounds;
        output.Bounds = _outputLayoutBounds;

        try
        {
            RenderComponentParameters(canvas, graphics, _chat, style);
        }
        finally
        {
            output.Bounds = strip;
        }
    }
}
