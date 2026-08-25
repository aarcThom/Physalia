// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Physalia.Core.Pdf;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Physalia.Core.Tests.Pdf;

/// <summary>
/// Exercises the reader against PDFs built here rather than committed as binary fixtures. PdfPig
/// writes as well as reads, so the input stays readable, diffable, and obviously correct about what
/// it contains — which matters most for the scanned-page case, where the whole assertion is that a
/// page has no text layer.
/// </summary>
public sealed class PdfTextReaderTests : IDisposable
{
    private readonly string _dir;

    public PdfTextReaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "physalia-pdf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    [Fact]
    public void Probe_ReportsPageCountSizeAndTextLayer()
    {
        string path = WriteSheetSet();
        PdfDescriptor d = PdfTextReader.Probe(path);

        Assert.Equal(2, d.PageCount);
        Assert.Equal(2, d.TextPageCount);
        Assert.False(d.IsFullyScanned);
        Assert.Equal(841, d.Pages[0].WidthPts, 0);
        Assert.All(d.Pages, p => Assert.True(p.HasTextLayer));
    }

    [Fact]
    public void Probe_DerivesTheAliasFromTheFileName()
    {
        string path = Path.Combine(_dir, "A-101 Floor Plan.pdf");
        File.WriteAllBytes(path, BuildSheetSet());
        Assert.Equal("a-101-floor-plan", PdfTextReader.Probe(path).Alias);
    }

    [Fact]
    public void Probe_GuessesTheSheetNumberFromTheTitleBlockCorner()
    {
        // The largest text in the bottom-right corner. This is what turns a page list into a sheet
        // list, which is the difference between the model asking for A-102 and paging blindly.
        PdfDescriptor d = PdfTextReader.Probe(WriteSheetSet());
        Assert.Equal("A-101", d.Pages[0].TitleBlockGuess);
        Assert.Equal("A-102", d.Pages[1].TitleBlockGuess);
    }

    [Fact]
    public void Probe_PageWithNoTextIsReportedAsHavingNoTextLayer()
    {
        string path = Path.Combine(_dir, "scanned.pdf");
        var builder = new PdfDocumentBuilder();
        builder.AddPage(600, 400);   // geometry only, no glyphs — a raster/vector-only sheet
        File.WriteAllBytes(path, builder.Build());

        PdfDescriptor d = PdfTextReader.Probe(path);
        Assert.False(d.Pages[0].HasTextLayer);
        Assert.True(d.IsFullyScanned);
        Assert.Equal(0, d.TextPageCount);
        Assert.Null(d.Pages[0].TitleBlockGuess);
    }

    [Fact]
    public void ExtractText_ReturnsTextInReadingOrderForTheRequestedPages()
    {
        PdfTextResult result = PdfTextReader.ExtractText(WriteSheetSet(), new[] { 1 }, maxChars: 5000);

        Assert.Contains("SECTION A-A", result.Text, StringComparison.Ordinal);
        Assert.Contains("--- page 1 ---", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("SECTION B-B", result.Text, StringComparison.Ordinal);
        Assert.Equal(new[] { 1 }, result.IncludedPages);
        Assert.Empty(result.EmptyPages);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void ExtractText_OutOfRangePagesAreIgnored()
    {
        PdfTextResult result = PdfTextReader.ExtractText(WriteSheetSet(), new[] { 1, 99 }, 5000);
        Assert.Equal(new[] { 1 }, result.IncludedPages);
    }

    [Fact]
    public void ExtractText_TruncatesToMaxCharsAndSaysSo()
    {
        PdfTextResult result = PdfTextReader.ExtractText(WriteSheetSet(), new[] { 1, 2 }, maxChars: 30);
        Assert.True(result.Truncated);
        Assert.True(result.Text.Length <= 60);
    }

    [Fact]
    public void ExtractText_ATextlessPageIsReportedSeparatelyNotSilentlyDropped()
    {
        // The load-bearing case. A scanned page and a blank page extract identically, so the empty
        // list is the only thing that stops the model concluding the sheet has nothing on it.
        string path = Path.Combine(_dir, "mixed.pdf");
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        PdfPageBuilder p1 = builder.AddPage(600, 400);
        p1.AddText("REAL TEXT", 12, new PdfPoint(50, 350), font);
        builder.AddPage(600, 400);
        File.WriteAllBytes(path, builder.Build());

        PdfTextResult result = PdfTextReader.ExtractText(path, new[] { 1, 2 }, 5000);

        Assert.Equal(new[] { 1 }, result.IncludedPages);
        Assert.Equal(new[] { 2 }, result.EmptyPages);
        Assert.Contains("REAL TEXT", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_FindsATermAndReportsWhereItSits()
    {
        IReadOnlyList<PdfSearchHit> hits = PdfTextReader.Search(WriteSheetSet(), "SECTION", maxHits: 10);

        Assert.NotEmpty(hits);
        PdfSearchHit first = hits[0];
        Assert.Equal(1, first.Page);
        Assert.Contains("SECTION", first.Text, StringComparison.Ordinal);

        // Drawn near the top of the sheet, so in top-left-origin coordinates it sits low on Y.
        Assert.InRange(first.Region.Y, 0.0, 0.5);
        Assert.True(first.Region.Width > 0);
    }

    [Fact]
    public void Search_IsCaseInsensitive() =>
        Assert.NotEmpty(PdfTextReader.Search(WriteSheetSet(), "section a-a", maxHits: 10));

    [Fact]
    public void Search_MatchesAcrossTheSpaceBetweenWords()
    {
        // Letters are grouped into rows before matching, so a multi-word term still hits. Matching
        // glyph by glyph would silently miss every phrase the model actually searches for.
        Assert.NotEmpty(PdfTextReader.Search(WriteSheetSet(), "SECTION A-A", maxHits: 10));
    }

    [Fact]
    public void Search_NoMatchReturnsEmpty() =>
        Assert.Empty(PdfTextReader.Search(WriteSheetSet(), "NOTHING LIKE THIS", maxHits: 10));

    [Fact]
    public void Search_RespectsMaxHits() =>
        Assert.Single(PdfTextReader.Search(WriteSheetSet(), "SECTION", maxHits: 1));

    [Fact]
    public void Search_BlankQueryReturnsNothingRatherThanEverything() =>
        Assert.Empty(PdfTextReader.Search(WriteSheetSet(), "   ", maxHits: 10));

    [Fact]
    public void Probe_BlankPathThrows() =>
        Assert.Throws<ArgumentException>(() => PdfTextReader.Probe("  "));

    /// <summary>
    /// Writes a two-page A1 drawing set with a title block on each sheet.
    /// </summary>
    /// <returns>The path of the written file.</returns>
    private string WriteSheetSet()
    {
        string path = Path.Combine(_dir, "sheets.pdf");
        File.WriteAllBytes(path, BuildSheetSet());
        return path;
    }

    /// <summary>
    /// Builds the two-page fixture: body text near the top, a large sheet number in the
    /// bottom-right title-block corner.
    /// </summary>
    /// <returns>The PDF bytes.</returns>
    private static byte[] BuildSheetSet()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        PdfPageBuilder p1 = builder.AddPage(841, 594);
        p1.AddText("SECTION A-A", 18, new PdfPoint(60, 500), font);
        p1.AddText("SCALE 1:50", 10, new PdfPoint(60, 470), font);
        p1.AddText("A-101", 24, new PdfPoint(700, 40), font);

        PdfPageBuilder p2 = builder.AddPage(841, 594);
        p2.AddText("SECTION B-B", 18, new PdfPoint(60, 500), font);
        p2.AddText("A-102", 24, new PdfPoint(700, 40), font);

        return builder.Build();
    }
}
