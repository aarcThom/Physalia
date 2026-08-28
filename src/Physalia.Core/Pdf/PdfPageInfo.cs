// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Globalization;

namespace Physalia.Core.Pdf;

/// <summary>
/// A one-page summary produced by <see cref="PdfTextReader.Probe(string)"/> without extracting the
/// page's content: its size, whether it has a text layer at all, and a best-effort guess at the
/// sheet identifier printed in its title block.
/// </summary>
/// <param name="Number">The 1-based page number.</param>
/// <param name="WidthPts">Page width in PDF points (72 per inch).</param>
/// <param name="HeightPts">Page height in PDF points.</param>
/// <param name="HasTextLayer">
/// Whether the page carries any extractable glyphs. False means a scan or a pure-raster export:
/// text extraction will return nothing and the page has to be rendered and looked at instead.
/// </param>
/// <param name="TitleBlockGuess">
/// The largest-font text found in the bottom-right corner, where a drawing sheet's number normally
/// sits. A heuristic and nothing more — always reported to the model as a guess.
/// </param>
public sealed record PdfPageInfo(
    int Number,
    double WidthPts,
    double HeightPts,
    bool HasTextLayer,
    string? TitleBlockGuess)
{
    /// <summary>
    /// Gets the page size rendered for a human, in millimetres, with the common ISO/ANSI sheet name
    /// appended when it matches one. Architectural sets are talked about in sheet sizes, not points.
    /// </summary>
    public string SizeDescription
    {
        get
        {
            double wmm = WidthPts * 25.4 / 72.0;
            double hmm = HeightPts * 25.4 / 72.0;
            string? name = SheetName(wmm, hmm);
            string dims = string.Format(
                CultureInfo.InvariantCulture, "{0:F0}x{1:F0}mm", wmm, hmm);
            return name is null ? dims : $"{dims} ({name})";
        }
    }

    /// <summary>
    /// Matches a page size against the common ISO A-series and ANSI sheet sizes, in either
    /// orientation, with a tolerance wide enough to absorb the rounding a CAD exporter introduces.
    /// </summary>
    /// <param name="wmm">Page width in millimetres.</param>
    /// <param name="hmm">Page height in millimetres.</param>
    /// <returns>The sheet name, or null when nothing matches closely enough.</returns>
    private static string? SheetName(double wmm, double hmm)
    {
        (string Name, double W, double H)[] sizes =
        {
            ("A4", 210, 297), ("A3", 297, 420), ("A2", 420, 594),
            ("A1", 594, 841), ("A0", 841, 1189),
            ("ANSI A", 216, 279), ("ANSI B", 279, 432), ("ANSI C", 432, 559),
            ("ANSI D", 559, 864), ("ANSI E", 864, 1118),
            ("ARCH C", 457, 610), ("ARCH D", 610, 914), ("ARCH E", 914, 1219),
            ("ARCH E1", 762, 1067),
        };

        const double Tolerance = 6.0;
        foreach ((string name, double w, double h) in sizes)
        {
            bool portrait = Math.Abs(wmm - w) < Tolerance && Math.Abs(hmm - h) < Tolerance;
            bool landscape = Math.Abs(wmm - h) < Tolerance && Math.Abs(hmm - w) < Tolerance;
            if (portrait || landscape)
            {
                return landscape && !portrait ? name + " landscape" : name;
            }
        }

        return null;
    }
}
