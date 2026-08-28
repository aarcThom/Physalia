// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Physalia.Core.Pdf;
using Xunit;

namespace Physalia.Core.Tests.Pdf;

public class PdfPageRangeTests
{
    [Theory]
    [InlineData("3", new[] { 3 })]
    [InlineData("1-4", new[] { 1, 2, 3, 4 })]
    [InlineData("2,5,9", new[] { 2, 5, 9 })]
    [InlineData("1-3,7", new[] { 1, 2, 3, 7 })]
    [InlineData(" 1 - 3 , 7 ", new[] { 1, 2, 3, 7 })]
    public void Parse_ReadsTheSpellingsTheModelWrites(string spec, int[] expected) =>
        Assert.Equal(expected, PdfPageRange.Parse(spec, pageCount: 10, maxPages: 100));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("all")]
    [InlineData("ALL")]
    public void Parse_BlankOrAll_SelectsEveryPage(string? spec) =>
        Assert.Equal(new[] { 1, 2, 3 }, PdfPageRange.Parse(spec, pageCount: 3, maxPages: 100));

    [Fact]
    public void Parse_ClampsToTheDocument()
    {
        // A model asking for more than exists means "everything up to the end", and failing the
        // whole call over the overshoot costs a round trip to learn nothing.
        Assert.Equal(new[] { 8, 9, 10 }, PdfPageRange.Parse("8-99", pageCount: 10, maxPages: 100));
        Assert.Empty(PdfPageRange.Parse("40-50", pageCount: 10, maxPages: 100));
    }

    [Fact]
    public void Parse_SkipsGarbageFragmentsButKeepsTheGoodOnes() =>
        Assert.Equal(new[] { 1, 2, 5 }, PdfPageRange.Parse("1-2, banana, 5, -3", pageCount: 10, maxPages: 100));

    [Fact]
    public void Parse_DeduplicatesOverlappingFragments() =>
        Assert.Equal(new[] { 1, 2, 3, 4 }, PdfPageRange.Parse("1-3,2-4,1", pageCount: 10, maxPages: 100));

    [Fact]
    public void Parse_AcceptsAReversedRange() =>
        Assert.Equal(new[] { 3, 4, 5 }, PdfPageRange.Parse("5-3", pageCount: 10, maxPages: 100));

    [Fact]
    public void Parse_TruncatesToMaxPages()
    {
        // The guard that stops "all" on a 400-sheet set from being an expensive way to burn a
        // context window.
        IReadOnlyList<int> pages = PdfPageRange.Parse("all", pageCount: 400, maxPages: 5);
        Assert.Equal(5, pages.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, pages);
    }

    [Fact]
    public void Parse_ZeroPageDocument_SelectsNothing() =>
        Assert.Empty(PdfPageRange.Parse("1-4", pageCount: 0, maxPages: 100));

    [Theory]
    [InlineData(new int[0], "none")]
    [InlineData(new[] { 3 }, "3")]
    [InlineData(new[] { 1, 2, 3, 4 }, "1-4")]
    [InlineData(new[] { 1, 2, 3, 7 }, "1-3, 7")]
    [InlineData(new[] { 1, 3, 5 }, "1, 3, 5")]
    [InlineData(new[] { 1, 2, 5, 6, 7, 20 }, "1-2, 5-7, 20")]
    public void Describe_CollapsesRuns(int[] pages, string expected) =>
        Assert.Equal(expected, PdfPageRange.Describe(pages));

    [Fact]
    public void Describe_RoundTripsWhatParseProduces()
    {
        IReadOnlyList<int> parsed = PdfPageRange.Parse("1-3,7", pageCount: 10, maxPages: 100);
        Assert.Equal("1-3, 7", PdfPageRange.Describe(parsed));
        Assert.Equal(parsed, PdfPageRange.Parse(PdfPageRange.Describe(parsed), 10, 100).ToList());
    }
}
