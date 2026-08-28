// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Physalia.Core.Pdf;

/// <summary>
/// Reads text out of a PDF: probing a document's shape, extracting reading-ordered text for a page
/// selection, and locating a term on the page. Built on PdfPig, which is pure managed — the
/// RASTERIZER is a separate, native concern and lives in Physalia.GH.
///
/// <para>Every method takes a path and opens the file for the duration of the call. PDFs are
/// referenced in place and never copied, and an architectural set runs to hundreds of megabytes, so
/// nothing here holds a document open between calls or caches its bytes.</para>
/// </summary>
public static class PdfTextReader
{
    // Guards on the title-block heuristic. The corner is where a sheet number lives; the height
    // floor keeps body text and dimension strings from being mistaken for one.
    private const double TitleBlockCornerFraction = 0.28;
    private const double TitleBlockMinimumHeightPts = 7.0;
    private const int TitleBlockMaxLength = 40;

    /// <summary>
    /// Reads a document's shape without extracting its content: page count, page sizes, whether
    /// each page carries a text layer, and a guess at each page's sheet number.
    /// </summary>
    /// <param name="path">The absolute path of the PDF.</param>
    /// <param name="alias">The alias the model will address this document by.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="ArgumentException">Thrown when the path is blank.</exception>
    public static PdfDescriptor Probe(string path, string alias)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A PDF path is required.", nameof(path));
        }

        using PdfDocument document = PdfDocument.Open(path);
        var pages = new List<PdfPageInfo>(document.NumberOfPages);

        for (int number = 1; number <= document.NumberOfPages; number++)
        {
            Page page = document.GetPage(number);
            IReadOnlyList<Letter> letters = page.Letters;
            pages.Add(new PdfPageInfo(
                number,
                page.Width,
                page.Height,
                letters.Count > 0,
                GuessTitleBlock(page, letters)));
        }

        return new PdfDescriptor(path, alias, Path.GetFileName(path), pages);
    }

    /// <summary>
    /// Probes a document, deriving the alias from the file name.
    /// </summary>
    /// <param name="path">The absolute path of the PDF.</param>
    /// <returns>The descriptor.</returns>
    public static PdfDescriptor Probe(string path) =>
        Probe(path, PdfAliases.FromFileName(path));

    /// <summary>
    /// Extracts reading-ordered text for a page selection.
    ///
    /// <para>Uses PdfPig's content-order extractor rather than the raw page text. Raw text comes out
    /// in content-stream order, which on anything with columns or a title block interleaves
    /// unrelated fragments and reads as noise.</para>
    /// </summary>
    /// <param name="path">The absolute path of the PDF.</param>
    /// <param name="pages">1-based page numbers to read, in order.</param>
    /// <param name="maxChars">The character budget for the whole result.</param>
    /// <returns>The extracted text, the pages actually included, and whether it was truncated.</returns>
    public static PdfTextResult ExtractText(string path, IReadOnlyList<int> pages, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A PDF path is required.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(pages);

        using PdfDocument document = PdfDocument.Open(path);
        var builder = new StringBuilder();
        var included = new List<int>();
        var empty = new List<int>();
        bool truncated = false;

        foreach (int number in pages)
        {
            if (number < 1 || number > document.NumberOfPages)
            {
                continue;
            }

            Page page = document.GetPage(number);
            if (page.Letters.Count == 0)
            {
                // Recorded, not silently skipped: an image-only page is the single most likely
                // reason a caller gets less than it asked for, and it changes what to do next.
                empty.Add(number);
                continue;
            }

            string text = ContentOrderTextExtractor.GetText(page) ?? string.Empty;
            if (text.Length == 0)
            {
                empty.Add(number);
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append("--- page ").Append(number).Append(" ---\n");

            int remaining = maxChars - builder.Length;
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }

            if (text.Length > remaining)
            {
                builder.Append(text, 0, remaining);
                included.Add(number);
                truncated = true;
                break;
            }

            builder.Append(text);
            included.Add(number);
        }

        return new PdfTextResult(builder.ToString(), included, empty, truncated);
    }

    /// <summary>
    /// Finds every line containing a term and reports where each one sits on its page.
    /// </summary>
    /// <param name="path">The absolute path of the PDF.</param>
    /// <param name="query">The text to look for. Matching is case-insensitive and substring-based.</param>
    /// <param name="maxHits">The most hits to return.</param>
    /// <returns>The hits, in page order.</returns>
    public static IReadOnlyList<PdfSearchHit> Search(string path, string query, int maxHits)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A PDF path is required.", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(query) || maxHits <= 0)
        {
            return Array.Empty<PdfSearchHit>();
        }

        using PdfDocument document = PdfDocument.Open(path);
        var hits = new List<PdfSearchHit>();

        for (int number = 1; number <= document.NumberOfPages && hits.Count < maxHits; number++)
        {
            Page page = document.GetPage(number);
            if (page.Letters.Count == 0)
            {
                continue;
            }

            // Group into words, then into rows by vertical overlap, so a hit reports the phrase it
            // sits in rather than a single glyph — and so a term spanning a space still matches.
            foreach (LetterRow row in RowsOf(page.Letters))
            {
                if (row.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                hits.Add(new PdfSearchHit(number, row.Text.Trim(), row.ToRegion(page.Width, page.Height)));
                if (hits.Count >= maxHits)
                {
                    break;
                }
            }
        }

        return hits;
    }

    /// <summary>
    /// Groups a page's letters into rows of text by vertical overlap, preserving left-to-right
    /// order within each row and inserting a space where letters are visibly separated.
    /// </summary>
    /// <param name="letters">The page's letters.</param>
    /// <returns>The rows, in the order their baselines were first encountered.</returns>
    private static IEnumerable<LetterRow> RowsOf(IReadOnlyList<Letter> letters)
    {
        var rows = new List<LetterRow>();

        foreach (Letter letter in letters)
        {
            // Group on the true BASELINE, not the glyph box. Glyph boxes disagree between
            // characters on the same line — a hyphen's box floats well above an 'A's — so grouping
            // or ordering by box geometry splits "A-101" across rows and scrambles its order.
            double baseline = letter.StartBaseLine.Y;
            double tolerance = Math.Max(letter.PointSize, 1.0) * 0.3;

            // Rotated text lands in rows of its own, which is correct: it is a different label.
            LetterRow? row = rows.FirstOrDefault(r => Math.Abs(r.Baseline - baseline) <= tolerance);
            if (row is null)
            {
                row = new LetterRow(baseline);
                rows.Add(row);
            }

            row.Add(letter);
        }

        // Top of page first, matching how the rows would be read.
        return rows.OrderByDescending(r => r.Baseline);
    }

    /// <summary>
    /// Guesses the sheet identifier by taking the largest-font text in the page's bottom-right
    /// corner, which is where a drawing sheet's title block sits.
    ///
    /// <para>A heuristic, and reported to the model as one. It is worth having because it lets a
    /// forty-sheet set be listed as sheet numbers rather than "page 1..40", which is the difference
    /// between the model asking for the right sheet and paging through blindly.</para>
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="letters">The page's letters.</param>
    /// <returns>The guessed sheet identifier, or null when nothing in the corner qualifies.</returns>
    private static string? GuessTitleBlock(Page page, IReadOnlyList<Letter> letters)
    {
        if (letters.Count == 0)
        {
            return null;
        }

        double minX = page.Width * (1 - TitleBlockCornerFraction);
        double maxY = page.Height * TitleBlockCornerFraction;

        List<Letter> corner = letters
            .Where(l => l.BoundingBox.Left >= minX && l.BoundingBox.Bottom <= maxY)
            .ToList();

        if (corner.Count == 0)
        {
            return null;
        }

        LetterRow? best = RowsOf(corner)
            .Where(r => r.MaxHeight >= TitleBlockMinimumHeightPts)
            .Where(r => r.Text.Trim().Length is > 0 and <= TitleBlockMaxLength)
            .OrderByDescending(r => r.MaxHeight)
            .FirstOrDefault();

        return best?.Text.Trim();
    }

    /// <summary>
    /// A run of letters sharing a baseline, accumulated left to right.
    /// </summary>
    private sealed class LetterRow
    {
        private readonly List<Letter> _letters = new();
        private string? _text;

        /// <summary>
        /// Initializes a new instance of the <see cref="LetterRow"/> class.
        /// </summary>
        /// <param name="baseline">The row's baseline, in points from the page bottom.</param>
        public LetterRow(double baseline) => Baseline = baseline;

        /// <summary>
        /// Gets the row's baseline, in points from the page bottom.
        /// </summary>
        public double Baseline { get; }

        /// <summary>
        /// Gets the largest point size in the row, used to rank title-block candidates.
        /// </summary>
        public double MaxHeight { get; private set; }

        /// <summary>
        /// Gets the row's text, ordered left to right with a space wherever the glyphs are visibly
        /// separated.
        /// </summary>
        public string Text => _text ??= BuildText();

        /// <summary>
        /// Adds a letter to the row. Order of addition does not matter — the text is assembled by
        /// horizontal position when it is first read.
        /// </summary>
        /// <param name="letter">The letter to add.</param>
        public void Add(Letter letter)
        {
            _letters.Add(letter);
            MaxHeight = Math.Max(MaxHeight, letter.PointSize);
            _text = null;
        }

        /// <summary>
        /// Converts the row's extent to a normalized, top-left-origin region.
        /// </summary>
        /// <param name="pageWidth">The page width in points.</param>
        /// <param name="pageHeight">The page height in points.</param>
        /// <returns>The region the row occupies.</returns>
        public PdfRegion ToRegion(double pageWidth, double pageHeight)
        {
            double left = _letters.Min(l => l.BoundingBox.Left);
            double right = _letters.Max(l => l.BoundingBox.Right);
            double bottom = _letters.Min(l => l.BoundingBox.Bottom);
            double top = _letters.Max(l => l.BoundingBox.Top);
            return PdfRegion.FromPdfPoints(left, bottom, right, top, pageWidth, pageHeight);
        }

        /// <summary>
        /// Assembles the row's text in reading order.
        /// </summary>
        /// <returns>The row text.</returns>
        private string BuildText()
        {
            var builder = new StringBuilder();
            double previousRight = double.NaN;

            foreach (Letter letter in _letters.OrderBy(l => l.BoundingBox.Left))
            {
                double left = letter.BoundingBox.Left;

                // Width of the letter itself is a poor yardstick — a period is narrow and would
                // manufacture spaces around it. Point size is the stable measure of the gap that
                // means "word break" at this text's scale.
                double gap = Math.Max(letter.PointSize, 1.0) * 0.2;
                if (!double.IsNaN(previousRight) && left - previousRight > gap)
                {
                    builder.Append(' ');
                }

                builder.Append(letter.Value);
                previousRight = Math.Max(previousRight, letter.BoundingBox.Right);
            }

            return builder.ToString();
        }
    }
}
