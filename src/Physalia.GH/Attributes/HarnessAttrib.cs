// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Physalia.GH.Harness;

namespace Physalia.GH.Attributes;

/// <summary>
/// Attributes for the harness proxy. The node is drawn as a distinct capsule — light-blue body,
/// black edge, pink-to-white inner rim — so a harness reads at a glance as a container rather than
/// a working component, and double-clicking it takes the canvas into the pipeline it holds.
/// </summary>
public class HarnessAttrib : PhyComponentAttributes
{
    // Harness tint: light-blue body, black capsule edge, dark-purple text, with a pink-to-white
    // secondary outline traced just inside the black edge.
    private static readonly Color HarnessFill = Color.FromArgb(255, 218, 243, 245);
    private static readonly Color HarnessEdge = Color.Black;
    private static readonly Color HarnessText = Color.FromArgb(255, 47, 8, 87);
    private static readonly Color HarnessGlow = Color.FromArgb(255, 236, 0, 150);

    // Width of the secondary gradient outline; about half straddles outside the 1px black edge.
    private const float GlowWidth = 1f;

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

    /// <summary>
    /// Takes the canvas into the harness document on double-click, the way a cluster opens.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled — the double-click is consumed to enter the harness.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        Selected = false;
        _harness.OpenInCanvas();
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

        RenderSmoothCapsule(canvas, graphics, new GH_PaletteStyle(HarnessFill, HarnessEdge, HarnessText));
        DrawHarnessGlow(graphics);
    }

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

    // Traces the capsule silhouette with a fat pen filled by a vertical pink-to-white gradient. A
    // CompoundArray restricts the stroke to the pen's inner half (GH's own inner-shine trick) so it
    // lands just inside the black edge instead of straddling it.
    private void DrawHarnessGlow(Graphics graphics)
    {
        var capsule = GH_Capsule.CreateCapsule(Bounds, GH_Palette.Hidden);
        try
        {
            capsule.SetJaggedEdges(false, false);
            GraphicsPath? outline = capsule.OutlineShape;
            if (outline is null)
            {
                return;
            }

            using var brush = new LinearGradientBrush(
                RectangleF.Inflate(Bounds, 2f, 2f), Color.White, HarnessGlow, LinearGradientMode.Vertical);

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
}
