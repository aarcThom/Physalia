// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Globalization;

namespace Physalia.Core.Pdf;

/// <summary>
/// A rectangular part of a page, in normalized page coordinates: 0..1 across and down, with the
/// origin at the TOP-LEFT of the page.
///
/// <para>Normalized rather than points, because the model has to be able to compose a region from
/// what it sees. A search hit reports one of these, a rendered overview is described by one, and
/// the next <c>render</c> call passes one back — all without the model knowing the sheet's size or
/// the DPI anything was rendered at.</para>
///
/// <para>Top-left origin rather than the PDF's own bottom-left, because it agrees with how the
/// model reads a rendered image. The flip happens once, at each edge of this type — PdfPig reports
/// glyph boxes bottom-up and PDFium's crop rectangle is top-down, so anything working in page
/// points converts through <see cref="ToPointsTopLeft"/> instead of doing its own arithmetic.</para>
/// </summary>
/// <param name="X">Left edge, 0..1 from the left of the page.</param>
/// <param name="Y">Top edge, 0..1 from the TOP of the page.</param>
/// <param name="Width">Width as a fraction of page width.</param>
/// <param name="Height">Height as a fraction of page height.</param>
public readonly record struct PdfRegion(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// Gets the region covering the whole page.
    /// </summary>
    public static PdfRegion Full => new(0, 0, 1, 1);

    /// <summary>
    /// Gets a value indicating whether this region covers the entire page, in which case a caller
    /// can skip cropping altogether.
    /// </summary>
    public bool IsFullPage =>
        X <= 0.0001 && Y <= 0.0001 && Width >= 0.9999 && Height >= 0.9999;

    /// <summary>
    /// Builds a region from a PdfPig glyph rectangle, whose coordinates are in page points measured
    /// from the BOTTOM-left, flipping it to this type's top-left convention.
    /// </summary>
    /// <param name="left">Left edge in points.</param>
    /// <param name="bottom">Bottom edge in points, measured up from the page's bottom.</param>
    /// <param name="right">Right edge in points.</param>
    /// <param name="top">Top edge in points, measured up from the page's bottom.</param>
    /// <param name="pageWidthPts">The page width in points.</param>
    /// <param name="pageHeightPts">The page height in points.</param>
    /// <returns>The equivalent normalized, top-left-origin region, clamped to the page.</returns>
    public static PdfRegion FromPdfPoints(
        double left, double bottom, double right, double top, double pageWidthPts, double pageHeightPts)
    {
        if (pageWidthPts <= 0 || pageHeightPts <= 0)
        {
            return Full;
        }

        double x = left / pageWidthPts;
        double y = (pageHeightPts - top) / pageHeightPts;
        double w = (right - left) / pageWidthPts;
        double h = (top - bottom) / pageHeightPts;
        return new PdfRegion(x, y, w, h).Clamped();
    }

    /// <summary>
    /// Expands this region by a margin on every side, expressed as a fraction of the page, and
    /// clamps the result to the page. Used to give a search hit some context before it is rendered:
    /// a crop tight to the glyph boxes shows the words and nothing they refer to.
    /// </summary>
    /// <param name="margin">The margin to add on each side, as a page fraction.</param>
    /// <returns>The padded region.</returns>
    public PdfRegion Padded(double margin) =>
        new PdfRegion(X - margin, Y - margin, Width + (margin * 2), Height + (margin * 2)).Clamped();

    /// <summary>
    /// Returns this region with its union taken against another, so a set of hits can be collapsed
    /// into one crop that contains them all.
    /// </summary>
    /// <param name="other">The region to include.</param>
    /// <returns>The smallest region containing both.</returns>
    public PdfRegion Union(PdfRegion other)
    {
        double left = Math.Min(X, other.X);
        double top = Math.Min(Y, other.Y);
        double right = Math.Max(X + Width, other.X + other.Width);
        double bottom = Math.Max(Y + Height, other.Y + other.Height);
        return new PdfRegion(left, top, right - left, bottom - top).Clamped();
    }

    /// <summary>
    /// Clamps this region to the unit square, guaranteeing a non-degenerate result.
    /// </summary>
    /// <returns>The clamped region.</returns>
    public PdfRegion Clamped()
    {
        double x = Math.Clamp(X, 0, 1);
        double y = Math.Clamp(Y, 0, 1);
        double w = Math.Clamp(Width, MinimumExtent, 1 - x);
        double h = Math.Clamp(Height, MinimumExtent, 1 - y);
        return new PdfRegion(x, y, Math.Max(w, MinimumExtent), Math.Max(h, MinimumExtent));
    }

    /// <summary>
    /// Converts this region to page points with a TOP-LEFT origin, which is the coordinate space
    /// PDFium's crop rectangle uses.
    /// </summary>
    /// <param name="pageWidthPts">The page width in points.</param>
    /// <param name="pageHeightPts">The page height in points.</param>
    /// <returns>Left, top, width and height in points, measured down from the page's top edge.</returns>
    public (double Left, double Top, double Width, double Height) ToPointsTopLeft(
        double pageWidthPts, double pageHeightPts)
    {
        PdfRegion c = Clamped();
        return (c.X * pageWidthPts, c.Y * pageHeightPts, c.Width * pageWidthPts, c.Height * pageHeightPts);
    }

    /// <summary>
    /// Renders the region the way it is reported to the model and accepted back from it.
    /// </summary>
    /// <returns>The region as a compact coordinate string.</returns>
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "x={0:F3} y={1:F3} w={2:F3} h={3:F3}",
        X, Y, Width, Height);

    // A region is never allowed to collapse to nothing: PDFium given a zero-extent crop produces a
    // zero-pixel bitmap, which surfaces as an unexplained failure several layers away.
    private const double MinimumExtent = 0.001;
}
