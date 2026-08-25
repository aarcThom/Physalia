// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Pdf;
using Xunit;

namespace Physalia.Core.Tests.Pdf;

public class PdfToolRequestTests
{
    [Theory]
    [InlineData("list", PdfAction.List)]
    [InlineData("text", PdfAction.Text)]
    [InlineData("search", PdfAction.Search)]
    [InlineData("render", PdfAction.Render)]
    [InlineData("RENDER", PdfAction.Render)]
    [InlineData("  render  ", PdfAction.Render)]
    public void Parse_ReadsTheAction(string action, PdfAction expected) =>
        Assert.Equal(expected, PdfToolRequest.Parse($"{{\"action\":\"{action}\"}}").Action);

    [Theory]
    [InlineData("{\"action\":\"nonsense\"}")]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    public void Parse_UnusableInput_YieldsUnknownRatherThanThrowing(string? json)
    {
        // Every failure arm has to land on a working request. Throwing here would surface as an
        // unexplained tool error several layers away from the malformed JSON that caused it.
        PdfToolRequest request = PdfToolRequest.Parse(json);
        Assert.Equal(PdfAction.Unknown, request.Action);
        Assert.Equal(PdfToolRequest.DefaultMaxChars, request.MaxChars);
        Assert.Equal(PdfToolRequest.DefaultDpi, request.Dpi);
        Assert.Equal(1, request.Page);
        Assert.Null(request.Region);
    }

    [Fact]
    public void Parse_ReadsTheOrdinaryFields()
    {
        PdfToolRequest r = PdfToolRequest.Parse(
            "{\"action\":\"text\",\"alias\":\"a-101\",\"pages\":\"1-4\",\"max_chars\":1200}");

        Assert.Equal(PdfAction.Text, r.Action);
        Assert.Equal("a-101", r.Alias);
        Assert.Equal("1-4", r.Pages);
        Assert.Equal(1200, r.MaxChars);
    }

    [Fact]
    public void Parse_BlankStringsAreTreatedAsAbsent()
    {
        PdfToolRequest r = PdfToolRequest.Parse("{\"action\":\"list\",\"alias\":\"   \",\"query\":\"\"}");
        Assert.Null(r.Alias);
        Assert.Null(r.Query);
    }

    [Fact]
    public void Parse_NumbersMayArriveAsStrings()
    {
        // Models emit "2" for a number often enough that rejecting it is just a wasted round trip.
        PdfToolRequest r = PdfToolRequest.Parse("{\"action\":\"render\",\"page\":\"7\",\"dpi\":\"300\"}");
        Assert.Equal(7, r.Page);
        Assert.Equal(300, r.Dpi);
    }

    [Fact]
    public void Parse_DpiIsClampedToASaneBand()
    {
        Assert.Equal(900, PdfToolRequest.Parse("{\"action\":\"render\",\"dpi\":99999}").Dpi);
        Assert.Equal(36, PdfToolRequest.Parse("{\"action\":\"render\",\"dpi\":1}").Dpi);
    }

    [Fact]
    public void Parse_PageIsNeverBelowOne() =>
        Assert.Equal(1, PdfToolRequest.Parse("{\"action\":\"render\",\"page\":-4}").Page);

    [Fact]
    public void Parse_ReadsARegion()
    {
        PdfToolRequest r = PdfToolRequest.Parse(
            "{\"action\":\"render\",\"region\":{\"x\":0.5,\"y\":0.25,\"width\":0.2,\"height\":0.1}}");

        Assert.NotNull(r.Region);
        PdfRegion region = r.Region!.Value;
        Assert.Equal(0.5, region.X, 3);
        Assert.Equal(0.25, region.Y, 3);
        Assert.Equal(0.2, region.Width, 3);
        Assert.Equal(0.1, region.Height, 3);
    }

    [Fact]
    public void Parse_RegionAcceptsShortPropertyNames()
    {
        PdfToolRequest r = PdfToolRequest.Parse(
            "{\"action\":\"render\",\"region\":{\"x\":0.1,\"y\":0.1,\"w\":0.3,\"h\":0.4}}");

        Assert.NotNull(r.Region);
        Assert.Equal(0.3, r.Region!.Value.Width, 3);
        Assert.Equal(0.4, r.Region!.Value.Height, 3);
    }

    [Fact]
    public void Parse_IncompleteRegionIsIgnoredRatherThanHalfApplied()
    {
        // Half a rectangle is not a crop. Falling back to the whole page shows the model something
        // it can correct from; a rectangle with two guessed edges shows it the wrong part silently.
        Assert.Null(PdfToolRequest.Parse("{\"action\":\"render\",\"region\":{\"x\":0.5,\"y\":0.5}}").Region);
        Assert.Null(PdfToolRequest.Parse("{\"action\":\"render\",\"region\":\"top left\"}").Region);
    }

    [Fact]
    public void Parse_RegionIsClampedIntoThePage()
    {
        PdfToolRequest r = PdfToolRequest.Parse(
            "{\"action\":\"render\",\"region\":{\"x\":0.8,\"y\":0.9,\"width\":5,\"height\":5}}");

        PdfRegion region = r.Region!.Value;
        Assert.True(region.X + region.Width <= 1.0001);
        Assert.True(region.Y + region.Height <= 1.0001);
    }
}
