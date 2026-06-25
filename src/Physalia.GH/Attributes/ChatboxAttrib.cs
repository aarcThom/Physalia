// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Components;
using Physalia.GH.Harness;

namespace Physalia.GH.Attributes;

/// <summary>
/// Attributes for the Chatbox component. The chat UI lives in a standalone window (opened on
/// double-click); on the canvas the Chatbox doubles as the proxy node for its collapsible
/// harness group. Once it owns members, the node takes on the Prompter's look — a light-blue
/// body with a lavender-pink edge — so a harness Chatbox reads distinctly from a plain one.
/// Collapse is driven from the right-click menu and the chat window.
/// </summary>
public class ChatboxAttrib : GH_ComponentAttributes
{
    // Harness tint: light-blue body, black capsule edge, dark-purple text. A secondary outline
    // (HarnessGlow → white, top-to-bottom) is traced just outside the black edge.
    private static readonly Color HarnessFill = Color.FromArgb(255, 218, 243, 245);
    private static readonly Color HarnessEdge = Color.Black;
    private static readonly Color HarnessText = Color.FromArgb(255, 47, 8, 87);
    private static readonly Color HarnessGlow = Color.FromArgb(255, 236, 0, 150);

    // Width of the secondary gradient outline; ~half straddles outside the 1px black edge.
    private const float GlowWidth = 1f;

    private readonly Chatbox _chatbox;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatboxAttrib"/> class.
    /// </summary>
    /// <param name="chatbox">The Chatbox component that owns these attributes.</param>
    public ChatboxAttrib(Chatbox chatbox)
        : base(chatbox)
    {
        _chatbox = chatbox;
    }

    /// <inheritdoc/>
    protected override void Layout()
    {
        // This Chatbox may itself be a (plain) member of another harness — when that harness is
        // collapsed, hide it like any member: shrink to the collapse point and skip the proxy chrome.
        if (CollapseGuard.TryCollapseLayout(this))
        {
            return;
        }

        base.Layout();

        // While collapsed, keep the hidden members glued under this (possibly moved) proxy.
        _chatbox.Group.RefreshCollapsePoint();
    }

    /// <inheritdoc/>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        // Hidden as a collapsed member of another harness — draw nothing at all.
        if (CollapseGuard.IsCollapsed(this))
        {
            return;
        }

        // A plain Chatbox (owns no members) renders as a normal node; only the Objects channel of
        // a harness Chatbox gets the Prompter-style tint.
        if (channel != GH_CanvasChannel.Objects || _chatbox.Group.Count == 0)
        {
            base.Render(canvas, graphics, channel);
            return;
        }

        // Render the capsule ourselves so it reads like the Prompter: GH would force a jagged
        // "no inputs" left edge and (because the Chatbox is not preview-capable) the Hidden
        // palette. Rounding both edges and driving fill/edge/text from our own style sidesteps
        // both. The output grip shows only while expanded; a collapsed proxy carries no grips.
        var style = new GH_PaletteStyle(HarnessFill, HarnessEdge, HarnessText);
        RenderSmoothCapsule(canvas, graphics, style);

        // Secondary outline on top, hugging the inside of the black edge: a pink→white gradient
        // traced on the exact capsule shape. A CompoundArray restricts the fat pen to its inner
        // band (GH's own inner-shine trick), so the stroke stays inside the path and the black
        // edge survives on the outside.
        DrawHarnessGlow(graphics);
    }

    // Mirrors GH_ComponentAttributes.RenderComponentCapsule but rounds both edges
    // (SetJaggedEdges(false, false)) and draws the fill/edge/text from our own palette style,
    // skipping GH's jagged-left + Hidden-palette behaviour for a not-preview-capable component.
    // The output grip is added only while the harness is expanded.
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

            // No inputs ever; the output grip is the proxy's only grip and is hidden while collapsed.
            if (!_chatbox.Group.Collapsed)
            {
                foreach (IGH_Param output in _chatbox.Params.Output)
                {
                    capsule.AddOutputGrip(output.Attributes.OutputGrip.Y);
                }
            }

            graphics.SmoothingMode = SmoothingMode.HighQuality;
            canvas.SetSmartTextRenderingHint();

            if (!string.IsNullOrWhiteSpace(_chatbox.Message))
            {
                capsule.RenderEngine.RenderMessage(graphics, _chatbox.Message, style);
            }

            capsule.Render(graphics, style);

            bool iconMode = _chatbox.IconDisplayMode == GH_IconDisplayMode.icon
                || (_chatbox.IconDisplayMode == GH_IconDisplayMode.application && Grasshopper.CentralSettings.CanvasObjectIcons);

            if (iconMode)
            {
                Image? icon = _chatbox.Locked ? _chatbox.Icon_24x24_Locked : _chatbox.Icon_24x24;
                if (icon != null)
                {
                    capsule.RenderEngine.RenderIcon(graphics, icon, m_innerBounds);
                }
            }
            else
            {
                var text = GH_Capsule.CreateTextCapsule(
                    m_innerBounds, m_innerBounds, GH_Palette.Black, _chatbox.NickName,
                    GH_FontServer.LargeAdjusted, GH_Orientation.vertical_center, 3, 6);
                text.Render(graphics, Selected, _chatbox.Locked, hidden: false);
                text.Dispose();
            }

            RenderComponentParameters(canvas, graphics, _chatbox, style);
        }
        finally
        {
            capsule.Dispose();
        }
    }

    // Traces the exact capsule silhouette (jagged input edge included) with a fat pen filled by a
    // vertical pink→white gradient, restricted by a CompoundArray to the pen's inner half so the
    // stroke lands just inside the black edge. Drawn on top of the fill: the rim fades from pink
    // at the bottom to white on top, with the black capsule edge still visible outside it.
    private void DrawHarnessGlow(Graphics graphics)
    {
        var capsule = GH_Capsule.CreateCapsule(Bounds, GH_Palette.Hidden);
        try
        {
            // Both edges rounded, matching the capsule drawn in RenderSmoothCapsule.
            capsule.SetJaggedEdges(false, false);
            GraphicsPath? outline = capsule.OutlineShape;
            if (outline is null)
            {
                return;
            }

            // White at the top of the node, pink at the bottom.
            using var brush = new LinearGradientBrush(
                RectangleF.Inflate(Bounds, 2f, 2f), Color.White, HarnessGlow, LinearGradientMode.Vertical);

            // Pen centred on the path; CompoundArray draws only the inner band (1f is the inner
            // edge of the stroke, as in GH's InnerContourPen), keeping it inside the path.
            using var pen = new Pen(brush, GlowWidth * 2f)
            {
                LineJoin = LineJoin.Round,
                CompoundArray = new[] { 0.5f, 1f },
            };

            SmoothingMode prev = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawPath(pen, outline);
            graphics.SmoothingMode = prev;
        }
        finally
        {
            capsule.Dispose();
        }
    }

    /// <summary>
    /// Opens the chat window on double-click.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled — the double-click is consumed to open the window.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        _chatbox.OpenWindow();
        _chatbox.Attributes.Selected = false;
        sender.Refresh();
        return GH_ObjectResponse.Handled;
    }
}
