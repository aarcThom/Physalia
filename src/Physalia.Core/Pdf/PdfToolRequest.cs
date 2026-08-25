// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Text.Json;

namespace Physalia.Core.Pdf;

/// <summary>
/// The action a <c>read_pdf</c> call is asking for.
/// </summary>
public enum PdfAction
{
    /// <summary>The call named no action, or one that does not exist.</summary>
    Unknown,

    /// <summary>Report every PDF available to this tool.</summary>
    List,

    /// <summary>Extract text for a page selection.</summary>
    Text,

    /// <summary>Find a term and report where on each page it sits.</summary>
    Search,

    /// <summary>Rasterize a page, or a region of one, and return it as an image.</summary>
    Render,
}

/// <summary>
/// One parsed <c>read_pdf</c> call.
///
/// <para>Parsing follows the house style set by <c>ReadUrl.ParseArgs</c>: every field is read
/// defensively and every failure arm falls back to a working default rather than throwing. A tool
/// call that is slightly malformed should still do something useful and report what it did — the
/// alternative spends a round trip teaching the model its own schema.</para>
/// </summary>
/// <param name="Action">The requested action.</param>
/// <param name="Alias">The PDF to act on. Optional for <see cref="PdfAction.List"/>.</param>
/// <param name="Pages">The page selection for <see cref="PdfAction.Text"/>.</param>
/// <param name="MaxChars">The character budget for extracted text.</param>
/// <param name="Query">The search term.</param>
/// <param name="Page">The 1-based page to render.</param>
/// <param name="Region">The part of the page to render, or null for the whole page.</param>
/// <param name="Dpi">The resolution to render at.</param>
public sealed record PdfToolRequest(
    PdfAction Action,
    string? Alias,
    string? Pages,
    int MaxChars,
    string? Query,
    int Page,
    PdfRegion? Region,
    int Dpi)
{
    /// <summary>
    /// The text character budget used when a call does not ask for one. Matches <c>read_url</c>.
    /// </summary>
    public const int DefaultMaxChars = 8000;

    /// <summary>
    /// The render resolution used when a call does not ask for one. Chosen so a whole ISO A1 sheet
    /// lands near the image size cap: rendering finer and then downscaling to fit buys nothing but
    /// time.
    /// </summary>
    public const int DefaultDpi = 150;

    /// <summary>
    /// The most hits a search reports.
    /// </summary>
    public const int MaxSearchHits = 40;

    // A render is bounded at both ends: too coarse is unreadable, too fine is a multi-second
    // PDFium call producing pixels that the image cap immediately throws away.
    private const int MinDpi = 36;
    private const int MaxDpi = 900;

    /// <summary>
    /// Parses a tool call's raw argument JSON.
    /// </summary>
    /// <param name="inputJson">The <c>input</c> object of the tool call, as JSON.</param>
    /// <returns>The parsed request; never null.</returns>
    public static PdfToolRequest Parse(string? inputJson)
    {
        var fallback = new PdfToolRequest(
            PdfAction.Unknown, null, null, DefaultMaxChars, null, 1, null, DefaultDpi);

        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return fallback;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(inputJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return fallback;
            }

            return new PdfToolRequest(
                ParseAction(ReadString(root, "action")),
                ReadString(root, "alias"),
                ReadString(root, "pages"),
                Math.Clamp(ReadInt(root, "max_chars", DefaultMaxChars), 200, 200_000),
                ReadString(root, "query"),
                Math.Max(1, ReadInt(root, "page", 1)),
                ReadRegion(root),
                Math.Clamp(ReadInt(root, "dpi", DefaultDpi), MinDpi, MaxDpi));
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// Maps the <c>action</c> string onto the enum.
    /// </summary>
    /// <param name="value">The raw action text.</param>
    /// <returns>The action, or <see cref="PdfAction.Unknown"/>.</returns>
    private static PdfAction ParseAction(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "list" => PdfAction.List,
        "text" => PdfAction.Text,
        "search" => PdfAction.Search,
        "render" => PdfAction.Render,
        _ => PdfAction.Unknown,
    };

    /// <summary>
    /// Reads the render region, accepting both the long and short property spellings.
    /// </summary>
    /// <param name="root">The argument object.</param>
    /// <returns>The region, or null when none was supplied or it was unusable.</returns>
    private static PdfRegion? ReadRegion(JsonElement root)
    {
        if (!root.TryGetProperty("region", out JsonElement region) ||
            region.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        double x = ReadDouble(region, "x", double.NaN);
        double y = ReadDouble(region, "y", double.NaN);
        double w = ReadDouble(region, "width", ReadDouble(region, "w", double.NaN));
        double h = ReadDouble(region, "height", ReadDouble(region, "h", double.NaN));

        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(w) || double.IsNaN(h))
        {
            return null;
        }

        return new PdfRegion(x, y, w, h).Clamped();
    }

    /// <summary>
    /// Reads a string property, treating a blank value as absent.
    /// </summary>
    /// <param name="root">The object to read from.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or null.</returns>
    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? (string.IsNullOrWhiteSpace(el.GetString()) ? null : el.GetString()!.Trim())
            : null;

    /// <summary>
    /// Reads an integer property, tolerating a numeric string.
    /// </summary>
    /// <param name="root">The object to read from.</param>
    /// <param name="name">The property name.</param>
    /// <param name="fallback">The value to use when absent or unreadable.</param>
    /// <returns>The value.</returns>
    private static int ReadInt(JsonElement root, string name, int fallback)
    {
        if (!root.TryGetProperty(name, out JsonElement el))
        {
            return fallback;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out int n) => n,
            JsonValueKind.Number when el.TryGetDouble(out double d) => (int)Math.Round(d),
            JsonValueKind.String when int.TryParse(el.GetString(), out int s) => s,
            _ => fallback,
        };
    }

    /// <summary>
    /// Reads a floating-point property, tolerating a numeric string.
    /// </summary>
    /// <param name="root">The object to read from.</param>
    /// <param name="name">The property name.</param>
    /// <param name="fallback">The value to use when absent or unreadable.</param>
    /// <returns>The value.</returns>
    private static double ReadDouble(JsonElement root, string name, double fallback)
    {
        if (!root.TryGetProperty(name, out JsonElement el))
        {
            return fallback;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDouble(out double d) => d,
            JsonValueKind.String when double.TryParse(
                el.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double s) => s,
            _ => fallback,
        };
    }
}
