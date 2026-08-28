// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Physalia.Core.Pdf;

/// <summary>
/// Parses the page selections the model writes — <c>"3"</c>, <c>"1-4"</c>, <c>"2,5,9"</c>,
/// <c>"1-3,7"</c>, <c>"all"</c> — into a clamped, ordered, duplicate-free list of 1-based page
/// numbers.
///
/// <para>Every malformed fragment is skipped rather than rejected. A model that writes
/// <c>"1-4, 99"</c> against a ten-page document means the first four pages, and failing the whole
/// call over the stray costs a round trip to learn nothing. What the caller reports back is which
/// pages it actually read, so an over-wide request corrects itself.</para>
/// </summary>
public static class PdfPageRange
{
    /// <summary>
    /// The token that selects every page.
    /// </summary>
    public const string AllToken = "all";

    /// <summary>
    /// Parses a page selection against a document of a known length.
    /// </summary>
    /// <param name="spec">
    /// The selection text. Blank or <c>"all"</c> selects every page up to <paramref name="maxPages"/>.
    /// </param>
    /// <param name="pageCount">The document's page count.</param>
    /// <param name="maxPages">
    /// The most pages the caller is willing to act on. The selection is truncated to this, which is
    /// what stops "all" on a 400-sheet set from being an expensive way to exhaust a context window.
    /// </param>
    /// <returns>Ordered, distinct, 1-based page numbers, each within the document.</returns>
    public static IReadOnlyList<int> Parse(string? spec, int pageCount, int maxPages)
    {
        if (pageCount <= 0 || maxPages <= 0)
        {
            return Array.Empty<int>();
        }

        if (string.IsNullOrWhiteSpace(spec) ||
            spec.Trim().Equals(AllToken, StringComparison.OrdinalIgnoreCase))
        {
            return Enumerable.Range(1, Math.Min(pageCount, maxPages)).ToList();
        }

        var pages = new List<int>();
        var seen = new HashSet<int>();

        foreach (string rawPart in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string part = rawPart.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            // A hyphen anywhere but the leading character is a range; a leading one is a negative
            // number, which is simply not a page.
            int dash = part.IndexOf('-', 1);
            if (dash > 0)
            {
                if (!TryPage(part[..dash], out int from) || !TryPage(part[(dash + 1)..], out int to))
                {
                    continue;
                }

                // Accept a reversed range rather than dropping it — the intent is unambiguous.
                if (from > to)
                {
                    (from, to) = (to, from);
                }

                for (int p = Math.Max(1, from); p <= Math.Min(pageCount, to); p++)
                {
                    if (seen.Add(p))
                    {
                        pages.Add(p);
                    }

                    if (pages.Count >= maxPages)
                    {
                        return pages;
                    }
                }
            }
            else if (TryPage(part, out int single) && single >= 1 && single <= pageCount)
            {
                if (seen.Add(single))
                {
                    pages.Add(single);
                }

                if (pages.Count >= maxPages)
                {
                    return pages;
                }
            }
        }

        return pages;
    }

    /// <summary>
    /// Renders a page list the way it is reported back to the model, collapsing runs into ranges so
    /// a long selection stays short to read.
    /// </summary>
    /// <param name="pages">Ordered page numbers.</param>
    /// <returns>A compact description such as <c>"1-4, 7"</c>, or "none" for an empty list.</returns>
    public static string Describe(IReadOnlyList<int> pages)
    {
        if (pages is null || pages.Count == 0)
        {
            return "none";
        }

        var parts = new List<string>();
        int runStart = pages[0];
        int previous = pages[0];

        for (int i = 1; i <= pages.Count; i++)
        {
            bool contiguous = i < pages.Count && pages[i] == previous + 1;
            if (contiguous)
            {
                previous = pages[i];
                continue;
            }

            parts.Add(runStart == previous
                ? runStart.ToString(CultureInfo.InvariantCulture)
                : string.Format(CultureInfo.InvariantCulture, "{0}-{1}", runStart, previous));

            if (i < pages.Count)
            {
                runStart = pages[i];
                previous = pages[i];
            }
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Parses one page number, tolerating surrounding whitespace.
    /// </summary>
    /// <param name="text">The fragment to parse.</param>
    /// <param name="page">The parsed page number.</param>
    /// <returns>True when the fragment is a page number.</returns>
    private static bool TryPage(string text, out int page) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out page);
}
