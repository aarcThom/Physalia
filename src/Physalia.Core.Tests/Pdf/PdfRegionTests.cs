// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Pdf;
using Xunit;

namespace Physalia.Core.Tests.Pdf;

public class PdfRegionTests
{
    [Fact]
    public void Full_IsRecognisedAsTheWholePage()
    {
        Assert.True(PdfRegion.Full.IsFullPage);
        Assert.False(new PdfRegion(0.1, 0.1, 0.5, 0.5).IsFullPage);
    }

    [Fact]
    public void FromPdfPoints_FlipsTheOriginToTopLeft()
    {
        // PdfPig reports glyph boxes measured UP from the page bottom; this type — and PDFium's
        // crop rectangle — measure DOWN from the top. Getting this backwards renders the mirror
        // image of the requested area, which looks like a blank crop on a title block.
        PdfRegion region = PdfRegion.FromPdfPoints(
            left: 0, bottom: 500, right: 100, top: 600, pageWidthPts: 1000, pageHeightPts: 1000);

        Assert.Equal(0.0, region.X, 3);
        Assert.Equal(0.4, region.Y, 3);   // 1000 - 600 = 400 from the top
        Assert.Equal(0.1, region.Width, 3);
        Assert.Equal(0.1, region.Height, 3);
    }

    [Fact]
    public void FromPdfPoints_BottomOfPageBecomesBottomOfRegion()
    {
        PdfRegion region = PdfRegion.FromPdfPoints(0, 0, 100, 50, 1000, 1000);
        Assert.Equal(0.95, region.Y, 3);
    }

    [Fact]
    public void FromPdfPoints_DegeneratePageFallsBackToFullPage() =>
        Assert.True(PdfRegion.FromPdfPoints(0, 0, 1, 1, 0, 0).IsFullPage);

    [Fact]
    public void ToPointsTopLeft_IsTheInverseOfNormalizing()
    {
        var region = new PdfRegion(0.25, 0.5, 0.25, 0.1);
        (double left, double top, double width, double height) = region.ToPointsTopLeft(800, 600);

        Assert.Equal(200, left, 3);
        Assert.Equal(300, top, 3);
        Assert.Equal(200, width, 3);
        Assert.Equal(60, height, 3);
    }

    [Fact]
    public void Clamped_KeepsTheRegionInsideThePage()
    {
        PdfRegion region = new PdfRegion(0.9, 0.9, 0.5, 0.5).Clamped();
        Assert.True(region.X + region.Width <= 1.0001);
        Assert.True(region.Y + region.Height <= 1.0001);
    }

    [Fact]
    public void Clamped_NeverCollapsesToZeroExtent()
    {
        // A zero-extent crop makes PDFium produce a zero-pixel bitmap, which surfaces as an
        // unexplained failure a long way from the region that caused it.
        PdfRegion region = new PdfRegion(0.5, 0.5, 0, 0).Clamped();
        Assert.True(region.Width > 0);
        Assert.True(region.Height > 0);
    }

    [Fact]
    public void Clamped_HandlesNegativeOrigin()
    {
        PdfRegion region = new PdfRegion(-1, -1, 0.5, 0.5).Clamped();
        Assert.Equal(0, region.X, 3);
        Assert.Equal(0, region.Y, 3);
    }

    [Fact]
    public void Padded_GrowsOnEverySideAndStaysOnThePage()
    {
        PdfRegion region = new PdfRegion(0.4, 0.4, 0.1, 0.1).Padded(0.05);
        Assert.Equal(0.35, region.X, 3);
        Assert.Equal(0.35, region.Y, 3);
        Assert.Equal(0.2, region.Width, 3);

        PdfRegion atEdge = new PdfRegion(0, 0, 0.1, 0.1).Padded(0.05);
        Assert.Equal(0, atEdge.X, 3);
    }

    [Fact]
    public void Union_ContainsBothRegions()
    {
        PdfRegion union = new PdfRegion(0.1, 0.1, 0.1, 0.1)
            .Union(new PdfRegion(0.5, 0.6, 0.1, 0.1));

        Assert.Equal(0.1, union.X, 3);
        Assert.Equal(0.1, union.Y, 3);
        Assert.Equal(0.5, union.Width, 3);
        Assert.Equal(0.6, union.Height, 3);
    }

    [Fact]
    public void ToString_UsesInvariantDecimalsSoTheModelNeverSeesACommaPoint()
    {
        // The model reads this string and hands the numbers straight back in the next call.
        Assert.Equal("x=0.250 y=0.500 w=0.100 h=0.200", new PdfRegion(0.25, 0.5, 0.1, 0.2).ToString());
    }
}
