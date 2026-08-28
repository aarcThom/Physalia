// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Physalia.Core.Common;

namespace Physalia.Core.Pdf;

/// <summary>
/// Renders every piece of text the PDF tools put in front of the model: the descriptor a freshly
/// attached PDF contributes to the user turn, and the result body of each <c>read_pdf</c> action.
///
/// <para>Kept pure and in one place so the wording is testable and consistent. The wording carries
/// real weight here — several of these strings exist specifically to stop the model drawing a wrong
/// conclusion from a technically-correct empty result.</para>
/// </summary>
public static class PdfReports
{
    // How many sheet guesses to list before summarising. Long enough to cover a normal package,
    // short enough that a 400-sheet set does not become the whole turn.
    private const int MaxListedSheets = 40;

    /// <summary>
    /// Renders the block of text a newly attached PDF contributes to the user's turn.
    ///
    /// <para>This is the entire cost of attaching a PDF. It deliberately carries no page content —
    /// what it carries is enough for the model to decide which pages are worth a tool call.</para>
    /// </summary>
    /// <param name="descriptors">The PDFs attached with this turn.</param>
    /// <returns>The descriptor text, or an empty string when nothing was attached.</returns>
    public static string DescribeAttachments(IReadOnlyList<PdfDescriptor> descriptors)
    {
        if (descriptors is null || descriptors.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append(descriptors.Count == 1 ? "[PDF attached]" : "[PDFs attached]").Append('\n');

        foreach (PdfDescriptor d in descriptors)
        {
            builder.Append(Summarise(d)).Append('\n');
        }

        builder.Append(
            "Use the read_pdf tool to read these — nothing of their content is included above. ")
            .Append("Call read_pdf with the alias shown.");

        return builder.ToString();
    }

    /// <summary>
    /// Renders the result of the <c>list</c> action.
    /// </summary>
    /// <param name="attached">PDFs the human attached this session.</param>
    /// <param name="folder">PDFs found in the node's configured folder.</param>
    /// <returns>The result body.</returns>
    public static string RenderList(
        IReadOnlyList<PdfDescriptor> attached, IReadOnlyList<PdfDescriptor> folder)
    {
        attached ??= Array.Empty<PdfDescriptor>();
        folder ??= Array.Empty<PdfDescriptor>();

        if (attached.Count == 0 && folder.Count == 0)
        {
            return "No PDFs are available. The human has attached none in this conversation, and " +
                   "this node's PDF Folder input is empty or contains no PDFs.";
        }

        var builder = new StringBuilder();

        if (attached.Count > 0)
        {
            builder.Append("Attached in this conversation:\n");
            foreach (PdfDescriptor d in attached)
            {
                builder.Append(Summarise(d)).Append('\n');
            }
        }

        if (folder.Count > 0)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append("Available from this node's PDF folder:\n");
            foreach (PdfDescriptor d in folder)
            {
                builder.Append(Summarise(d)).Append('\n');
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Renders the result of the <c>text</c> action.
    /// </summary>
    /// <param name="descriptor">The document read from.</param>
    /// <param name="result">What extraction produced.</param>
    /// <param name="requested">The pages that were asked for.</param>
    /// <returns>The result body.</returns>
    public static string RenderText(
        PdfDescriptor descriptor, PdfTextResult result, IReadOnlyList<int> requested)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();
        builder.Append(Inv($"{descriptor.DisplayName} ({descriptor.Alias}) — requested pages {PdfPageRange.Describe(requested)}."));

        if (result.EmptyPages.Count > 0)
        {
            // Never let an empty extraction read as an empty document. This sentence is the whole
            // difference between the model rendering the page and the model reporting it is blank.
            builder.Append('\n')
                .Append($"Pages {PdfPageRange.Describe(result.EmptyPages)} carry NO text layer — they are ")
                .Append("scanned or vector-only artwork, so there is no text to extract from them. To read ")
                .Append("those pages, call read_pdf again with action \"render\" and look at the image.");
        }

        if (result.Truncated)
        {
            builder.Append('\n').Append(
                "The text below was cut short by max_chars. Ask for a narrower page range, or a " +
                "larger max_chars, to see the rest.");
        }

        if (result.Text.Length > 0)
        {
            builder.Append("\n\n").Append(result.Text);
        }
        else if (result.EmptyPages.Count == 0)
        {
            builder.Append('\n').Append("No text was found on the requested pages.");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders the result of the <c>search</c> action.
    /// </summary>
    /// <param name="descriptor">The document searched.</param>
    /// <param name="query">The term searched for.</param>
    /// <param name="hits">The matches found.</param>
    /// <returns>The result body.</returns>
    public static string RenderSearch(
        PdfDescriptor descriptor, string query, IReadOnlyList<PdfSearchHit> hits)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        hits ??= Array.Empty<PdfSearchHit>();

        if (hits.Count == 0)
        {
            var miss = new StringBuilder();
            miss.Append(Inv($"No text match for \"{query}\" in {descriptor.DisplayName} ({descriptor.Alias})."));

            if (descriptor.IsFullyScanned)
            {
                miss.Append(' ').Append(
                    "No page in this document has a text layer at all, so search cannot work here — " +
                    "this is a scanned or vector-only document. Use action \"render\" and look at the pages.");
            }
            else if (descriptor.TextPageCount < descriptor.PageCount)
            {
                miss.Append($" Note only {descriptor.TextPageCount} of {descriptor.PageCount} pages carry a ")
                    .Append("text layer, so the term may be present on a page search cannot see.");
            }

            return miss.ToString();
        }

        var builder = new StringBuilder();
        builder.Append(Inv($"{hits.Count} match(es) for \"{query}\" in {descriptor.DisplayName} ({descriptor.Alias}):"));

        foreach (PdfSearchHit hit in hits)
        {
            builder.Append('\n').Append(Inv($"  page {hit.Page} — \"{Shorten(hit.Text, 90)}\" at {hit.Region}"));
        }

        builder.Append("\n\nTo look at any of these, call read_pdf with action \"render\", that page, ")
               .Append("and a region around the coordinates shown (widen it a little to get context).");

        return builder.ToString();
    }

    /// <summary>
    /// Renders the text that accompanies a rendered page image.
    ///
    /// <para>It states the resolution deliberately. An unreadable crop and an absent detail look
    /// identical to a model that does not know how much it is being shown, and the fix for the
    /// first one — crop tighter and render again — is something it has to be told is available.</para>
    /// </summary>
    /// <param name="descriptor">The document rendered from.</param>
    /// <param name="page">The page rendered.</param>
    /// <param name="region">The part of the page rendered.</param>
    /// <param name="pixelWidth">The delivered image width.</param>
    /// <param name="pixelHeight">The delivered image height.</param>
    /// <param name="downscaled">Whether the image was reduced to fit the delivery cap.</param>
    /// <returns>The result body.</returns>
    public static string RenderImageReport(
        PdfDescriptor descriptor,
        int page,
        PdfRegion region,
        int pixelWidth,
        int pixelHeight,
        bool downscaled)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var builder = new StringBuilder();
        builder.Append(Inv($"{descriptor.DisplayName} ({descriptor.Alias}) page {page}, rendered at {pixelWidth}x{pixelHeight} px."));
        builder.Append(region.IsFullPage
            ? " This is the whole page."
            : Inv($" This is the region {region} of the page, not the whole page."));

        if (downscaled)
        {
            builder.Append(Inv(
                $" It was downscaled to fit the {ImageLimits.MaxImageSide}px delivery limit."));
        }

        builder.Append(" The image follows in this same message.");

        if (region.IsFullPage)
        {
            // The single most common failure mode: a whole E-size sheet reduced to fit is legible
            // as a layout and illegible as text, and the model reports it cannot read the drawing
            // rather than asking for the part it needs.
            builder.Append(
                "\n\nIf any text or dimension in it is too small to read, that is the scale, not the " +
                "drawing: call read_pdf again with action \"render\", the same page, and a \"region\" " +
                "around the part you need. A region is {x, y, width, height} in 0-1 page fractions " +
                "measured from the TOP-LEFT of the page, and it is rendered at full resolution.");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders one document as a single summary line, with its sheet guesses.
    /// </summary>
    /// <param name="d">The document.</param>
    /// <returns>The summary.</returns>
    private static string Summarise(PdfDescriptor d)
    {
        var builder = new StringBuilder();
        builder.Append(Inv($"  \"{d.DisplayName}\" — alias `{d.Alias}`, {d.PageCount} page(s)"));

        string? size = d.Pages.Count > 0 ? d.Pages[0].SizeDescription : null;
        if (size is not null)
        {
            bool uniform = d.Pages.All(p => p.SizeDescription == size);
            builder.Append(uniform ? $", {size}" : $", mixed sizes (first page {size})");
        }

        if (d.IsFullyScanned)
        {
            builder.Append(", NO text layer on any page (scanned or vector-only — must be rendered to be read)");
        }
        else if (d.TextPageCount < d.PageCount)
        {
            builder.Append(Inv($", text layer on {d.TextPageCount} of {d.PageCount} pages"));
        }

        builder.Append('.');

        List<PdfPageInfo> titled = d.Pages.Where(p => !string.IsNullOrWhiteSpace(p.TitleBlockGuess)).ToList();
        if (titled.Count > 0)
        {
            builder.Append("\n    Sheet numbers (best guess, read from each page's title-block corner): ");
            builder.Append(string.Join(
                " · ",
                titled.Take(MaxListedSheets).Select(p => Inv($"p{p.Number} {p.TitleBlockGuess}"))));

            if (titled.Count > MaxListedSheets)
            {
                builder.Append(Inv($" … and {titled.Count - MaxListedSheets} more"));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Truncates text for a one-line report entry.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="max">The most characters to keep.</param>
    /// <returns>The shortened text.</returns>
    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    /// <summary>
    /// Formats an interpolated string with the invariant culture, so a page size or coordinate
    /// never reaches the model with a comma for a decimal point.
    /// </summary>
    /// <param name="handler">The interpolated string.</param>
    /// <returns>The formatted text.</returns>
    private static string Inv(FormattableString handler) =>
        handler.ToString(CultureInfo.InvariantCulture);
}
