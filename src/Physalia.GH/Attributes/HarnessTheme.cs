// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI.Canvas;

namespace Physalia.GH.Attributes;

/// <summary>
/// The Physalia look, shared by everything that belongs to the harness family: the proxy node, the
/// pill widgets shown inside a harness, and the harness panel.
///
/// <para>Light-blue body, black edge, dark-purple text, and a pink-to-white rim traced just inside
/// the edge. Kept in one place because these are the only nodes that opt out of Grasshopper's own
/// palette, and three copies of the same five colours drift.</para>
/// </summary>
internal static class HarnessTheme
{
    /// <summary>Body fill.</summary>
    internal static readonly Color Fill = Color.FromArgb(255, 218, 243, 245);

    /// <summary>Capsule edge.</summary>
    internal static readonly Color Edge = Color.Black;

    /// <summary>Text and glyph ink.</summary>
    internal static readonly Color Ink = Color.FromArgb(255, 47, 8, 87);

    /// <summary>The far end of the rim gradient; it runs from white at the top to this at the bottom.</summary>
    internal static readonly Color Glow = Color.FromArgb(255, 236, 0, 150);

    /// <summary>
    /// The pale purple between the pink and the blue. Inherited from the original Physalia panel, where
    /// it was the title strip's highlight — <see cref="Ink"/> is far too dark to stand in for it, since
    /// lightening a near-black purple only yields grey.
    /// </summary>
    internal static readonly Color Lilac = Color.FromArgb(255, 232, 188, 255);

    /// <summary>
    /// The saturated robin's-egg blue, for places that need to READ as blue rather than as a pale body.
    /// <see cref="Fill"/> is so close to white that behind any translucency it disappears. Inherited
    /// from the original Physalia panel, where it filled the entry section.
    /// </summary>
    internal static readonly Color Aqua = Color.FromArgb(255, 138, 194, 207);

    // Width of the rim stroke; about half of it straddles outside the 1px black edge.
    private const float GlowWidth = 1f;

    /// <summary>Gets the palette style a themed capsule is drawn with.</summary>
    internal static GH_PaletteStyle Style => new GH_PaletteStyle(Fill, Edge, Ink);

    /// <summary>
    /// Traces a capsule silhouette with a fat pen filled by a vertical pink-to-white gradient.
    ///
    /// <para>A CompoundArray restricts the stroke to the pen's inner half — Grasshopper's own
    /// inner-shine trick — so the rim lands just inside the black edge instead of straddling it.</para>
    /// </summary>
    /// <param name="graphics">The canvas graphics.</param>
    /// <param name="bounds">The capsule's bounds.</param>
    internal static void DrawGlow(Graphics graphics, RectangleF bounds)
    {
        var capsule = GH_Capsule.CreateCapsule(bounds, GH_Palette.Hidden);
        try
        {
            capsule.SetJaggedEdges(false, false);
            GraphicsPath? outline = capsule.OutlineShape;
            if (outline is null)
            {
                return;
            }

            using var brush = new LinearGradientBrush(
                RectangleF.Inflate(bounds, 2f, 2f), Color.White, Glow, LinearGradientMode.Vertical);

            using var pen = new Pen(brush, GlowWidth * 2f)
            {
                LineJoin = LineJoin.Round,
                CompoundArray = new[] { 0.5f, 1f },
            };

            SmoothingMode previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawPath(pen, outline);
            graphics.SmoothingMode = previous;
        }
        finally
        {
            capsule.Dispose();
        }
    }
}
