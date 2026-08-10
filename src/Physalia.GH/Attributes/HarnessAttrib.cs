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
    // How much wider the proxy is than the capsule Grasshopper would give it. A harness stands for a
    // whole pipeline, and with no parameters at all GH's own layout would make it one of the smallest
    // nodes on the canvas. Height is left alone — a node-height bar reads as a node.
    private const float WidthFactor = 3f;

    // Inset from the capsule to the region the icon (or the nickname) is drawn in, keeping it clear of
    // the gradient rim.
    private const float ContentInset = 2f;

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

    // The harness livery normally, Grasshopper's own selection palette while selected. Selection has to
    // read as selection — a node with a private colour scheme that ignores it looks broken next to every
    // other one — and GH_Skin is where that green comes from, so a canvas theme change follows along.
    private GH_PaletteStyle CapsuleStyle => Selected
        ? GH_Skin.palette_normal_selected
        : HarnessTheme.Style;

    /// <inheritdoc/>
    /// <remarks>
    /// The right-edge midpoint, where a Grasshopper output leaves from. The transmitter this stands in
    /// for reaches sideways to a script component on the same canvas, so a side grip both matches the
    /// platform and points the right way.
    /// </remarks>
    protected override PointF GripOrigin => RightCentre;

    /// <inheritdoc/>
    /// <remarks>The delegated arrow uses one proxy style regardless of which transmitter it drives.</remarks>
    public override WireGradient ArrowGradient => ArrowStyles.Proxy;

    /// <inheritdoc/>
    /// <remarks>
    /// The proxy's wire runs horizontally, leaving the right-edge grip rightwards and arriving with a
    /// rightward tip — matching where <see cref="GripOrigin"/> puts the grip.
    /// </remarks>
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
    /// <remarks>
    /// Widens the capsule, keeping its left edge where Grasshopper put it — so the node reaches
    /// rightwards from the spot it was placed at rather than jumping.
    /// </remarks>
    protected override RectangleF AdjustVisualBounds(RectangleF bounds) =>
        new(bounds.X, bounds.Y, bounds.Width * WidthFactor, bounds.Height);

    /// <inheritdoc/>
    /// <remarks>The grip is on the right, so the hittable strip goes there rather than underneath.</remarks>
    protected override RectangleF ExpandForGrip(RectangleF visual) =>
        new(visual.X, visual.Y, visual.Width + GripExpansion, visual.Height);

    /// <inheritdoc/>
    /// <remarks>Only expands the pick region for the grip when there is an arrow to host.</remarks>
    protected override void Layout()
    {
        base.Layout();

        // Grasshopper sized the inner region for the small capsule it thought it was laying out, so the
        // icon (or the nickname) would sit in a corner of the grown one. Re-centre it on the capsule.
        m_innerBounds = RectangleF.Inflate(VisualBounds, -ContentInset, -ContentInset);

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
            // Grip first, so the capsule paints over its inner half and only the outer part peeks past
            // the node's edge — the same look the transmitters used to have.
            if (HasArrow)
            {
                DrawGrip(graphics, GripOrigin);
            }

            RenderSmoothCapsule(canvas, graphics, CapsuleStyle);

            // The rim is drawn in both states: it is the harness's signature, and the point of the
            // selection colour is to answer "is this selected", which the body already does.
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

            // No RenderMessage call: the proxy deliberately carries no message tag, so the black
            // caption Grasshopper hangs under a node can never appear beneath a harness.
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
