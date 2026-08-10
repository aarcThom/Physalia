// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.GH.Attributes.UiElements;
using Physalia.GH.Components;
using Physalia.GH.Harness;

namespace Physalia.GH.Attributes;

/// <summary>
/// Attributes for the harness proxy. The node is drawn as a distinct capsule — light-blue body,
/// black edge, pink-to-white inner rim — so a harness reads at a glance as a container rather than
/// a working component.
///
/// <para>The proxy is the ONLY door onto the chat window: <b>double-click opens it</b> on the Chat
/// inside, and <b>right-click → "Edit Harness"</b> takes the canvas inside. The Chat itself answers
/// no gesture — it lives in the sub-document, where the user only reaches it deliberately. When the harness
/// holds exactly one transmitter the proxy also grows that transmitter's drag arrow, so the drag
/// happens on the canvas where its target actually lives — see <see cref="IHarnessArrow"/>.</para>
/// </summary>
public class HarnessAttrib : ArrowAttributeBase
{
    private readonly HarnessComponent _harness;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessAttrib"/> class.
    /// </summary>
    /// <param name="harness">The harness component that owns these attributes.</param>
    public HarnessAttrib(HarnessComponent harness)
        : base(harness)
    {
        _harness = harness;
    }

    /// <inheritdoc/>
    /// <remarks>The delegated arrow uses one proxy style regardless of which transmitter it drives.</remarks>
    public override WireGradient ArrowGradient => ArrowStyles.Proxy;

    /// <inheritdoc/>
    /// <remarks>The proxy arrow terminates horizontally (rightward tip) toward its target.</remarks>
    public override bool HorizontalArrow => true;

    /// <inheritdoc/>
    public override IEnumerable<PointF> SettledEndpoints(GH_Document doc)
        => _harness.TryGetSoleArrow(out IHarnessArrow? arrow) && arrow is not null
            ? arrow.GetArrowEndpoints(doc)
            : Array.Empty<PointF>();

    /// <inheritdoc/>
    public override void OnDrop(GH_Document doc, PointF dropPoint, bool ctrl)
    {
        if (_harness.TryGetSoleArrow(out IHarnessArrow? arrow) && arrow is not null)
        {
            arrow.HandleDrop(doc, dropPoint, ctrl);
        }
    }

    /// <summary>
    /// Opens the chat window on double-click, on the Chat inside the harness. The proxy stands in
    /// for that Chat, so it answers the same gesture; entering the harness is the right-click item.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled when a Chat was found; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (_harness.FindChat() is not { } chat)
        {
            return base.RespondToMouseDoubleClick(sender, e);
        }

        chat.OpenWindow();
        Selected = false;
        sender.Refresh();
        return GH_ObjectResponse.Handled;
    }

    /// <inheritdoc/>
    /// <remarks>Only expands the pick region for the grip when there is an arrow to host.</remarks>
    protected override void Layout()
    {
        base.Layout();

        if (!HasArrow)
        {
            Bounds = VisualBounds;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Composed by hand rather than through the base render: the harness draws a bespoke capsule,
    /// so it cannot fall through to <see cref="GH_ComponentAttributes"/>'s.
    /// </remarks>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        // The capsule, grip and arrow all draw against the un-expanded bounds; restore the pick
        // region afterwards so hit-testing still covers the grip strip.
        RectangleF outer = Bounds;
        Bounds = VisualBounds;

        if (channel == GH_CanvasChannel.Objects)
        {
            // Grip first, so the capsule paints over its top half and only the lower part peeks out
            // below the node — the same look the transmitters used to have.
            if (HasArrow)
            {
                DrawGrip(graphics, BottomCentre);
            }

            RenderSmoothCapsule(canvas, graphics, HarnessTheme.Style);
            HarnessTheme.DrawGlow(graphics, Bounds);
        }

        // The arrow wires (Wires channel).
        RenderGripContent(canvas, graphics, channel);

        Bounds = outer;
    }

    /// <inheritdoc/>
    /// <remarks>A press only starts a drag when there is a transmitter to delegate the drop to.</remarks>
    protected override bool TryStartDrag(GH_Canvas sender, GH_CanvasMouseEvent e)
        => HasArrow && base.TryStartDrag(sender, e);

    // Whether the harness holds exactly one transmitter, and so should show a drag arrow at all.
    private bool HasArrow => _harness.TryGetSoleArrow(out _);

    // Mirrors GH_ComponentAttributes.RenderComponentCapsule but rounds both edges and drives
    // fill/edge/text from our own palette style: the harness has no parameters at all, so GH would
    // otherwise force a jagged "no inputs" left edge and, because the node is not preview-capable,
    // the Hidden palette.
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

            graphics.SmoothingMode = SmoothingMode.HighQuality;
            canvas.SetSmartTextRenderingHint();

            if (!string.IsNullOrWhiteSpace(_harness.Message))
            {
                capsule.RenderEngine.RenderMessage(graphics, _harness.Message, style);
            }

            capsule.Render(graphics, style);

            bool iconMode = _harness.IconDisplayMode == GH_IconDisplayMode.icon
                || (_harness.IconDisplayMode == GH_IconDisplayMode.application && Grasshopper.CentralSettings.CanvasObjectIcons);

            if (iconMode)
            {
                Image? icon = _harness.Locked ? _harness.Icon_24x24_Locked : _harness.Icon_24x24;
                if (icon is not null)
                {
                    capsule.RenderEngine.RenderIcon(graphics, icon, m_innerBounds);
                }
            }
            else
            {
                var text = GH_Capsule.CreateTextCapsule(
                    m_innerBounds, m_innerBounds, GH_Palette.Black, _harness.NickName,
                    GH_FontServer.LargeAdjusted, GH_Orientation.vertical_center, 3, 6);
                text.Render(graphics, Selected, _harness.Locked, hidden: false);
                text.Dispose();
            }
        }
        finally
        {
            capsule.Dispose();
        }
    }

}
