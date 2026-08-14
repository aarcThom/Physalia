// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI.Canvas;
using Physalia.GH.Attributes;
using Physalia.GH.Harness;

namespace Physalia.GH.Widgets;

/// <summary>
/// Washes the canvas in a pink-to-purple-to-blue sweep while you are inside a harness document, the
/// way Grasshopper tints the canvas while you are inside a cluster — so a secondary screen never looks
/// like the file you started from.
///
/// <para>Not a widget: widgets paint at the very END of the pipeline, over the components, whereas a
/// background has to go under them. It hangs off <see cref="GH_Canvas.CanvasPaintBackground"/>
/// instead, which is raised once the grid is down and before groups, wires and objects — so the wash
/// tints the grid and everything else is drawn on top of it.</para>
///
/// <para>Grasshopper's own cluster tint is not reused: it keys on <c>GH_Document.Owner</c>, which
/// Physalia deliberately never sets (setting it hands the document a cluster menu whose "Save and
/// Return" disposes it — see <see cref="HarnessComponent"/>).</para>
/// </summary>
internal static class HarnessCanvasTint
{
    // How strongly the wash covers the canvas, out of 255. Every stop is pale, so this is high enough
    // to read as colour at a glance while leaving Grasshopper's grid visible through it.
    private const int WashAlpha = 65;

    // The pink end. HarnessTheme.Glow lightened towards white: the rim's magenta at full strength would
    // fight the components sitting on top of it.
    private static readonly Color WashPink = Color.FromArgb(255, 247, 153, 213);

    /// <summary>
    /// Starts tinting a canvas. Idempotent: <see cref="Paint"/> is a static method, so the delegate
    /// compares equal every time and the removal drops any earlier subscription — which matters
    /// because the widget-list event this is attached from can fire more than once for one canvas.
    /// </summary>
    /// <param name="canvas">The canvas to tint while it shows a harness. Null is ignored.</param>
    internal static void Attach(GH_Canvas? canvas)
    {
        if (canvas is null)
        {
            return;
        }

        canvas.CanvasPaintBackground -= Paint;
        canvas.CanvasPaintBackground += Paint;
    }

    // Fills the visible canvas with the wash, but only while the canvas is showing a harness.
    private static void Paint(GH_Canvas sender)
    {
        if (sender?.Graphics is null || HarnessComponent.OwnerOf(sender.Document) is null)
        {
            return;
        }

        Rectangle window = sender.ClientRectangle;
        if (window.Width <= 0 || window.Height <= 0)
        {
            return; // a zero-sized rect has no gradient axis, and LinearGradientBrush throws on one
        }

        Graphics graphics = sender.Graphics;

        // Device space, like the harness pills: the wash covers the window rather than a patch of
        // canvas, so it must not be scaled or panned by the viewport transform.
        Matrix oldTransform = graphics.Transform;
        graphics.ResetTransform();

        // Inflated by a pixel because GDI+ samples a linear gradient's first row/column from the far
        // end of the ramp, which shows up as a stray line of the wrong colour along two edges.
        Rectangle ramp = Rectangle.Inflate(window, 1, 1);

        using (var wash = new LinearGradientBrush(
            ramp, Color.Empty, Color.Empty, LinearGradientMode.ForwardDiagonal))
        {
            // Three stops, so InterpolationColors — which supersedes the two colours passed above.
            // Diagonal: the sweep reads across the window whatever its proportions, where a vertical
            // one collapses to a thin band on a wide, short canvas.
            // Blue takes most of the ramp. Pink is by far the most assertive of the three, so an even
            // split reads as a pink canvas with the far corner fading out; turning the corner early and
            // ending on a blue that is genuinely saturated is what makes the sweep land as
            // pink-through-purple-into-blue.
            wash.InterpolationColors = new ColorBlend
            {
                Colors = new[]
                {
                    Color.FromArgb(WashAlpha, WashPink),
                    Color.FromArgb(WashAlpha, HarnessTheme.Lilac),
                    Color.FromArgb(WashAlpha, HarnessTheme.Aqua),
                },
                Positions = new[] { 0f, 0.32f, 1f },
            };

            graphics.FillRectangle(wash, window);
        }

        graphics.Transform = oldTransform;
        oldTransform.Dispose();
    }
}
