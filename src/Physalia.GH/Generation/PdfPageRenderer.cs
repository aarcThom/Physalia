// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Drawing;
using System.IO;
using PDFtoImage;
using Physalia.Core.Common;
using Physalia.Core.Pdf;
using SkiaSharp;

namespace Physalia.GH.Generation;

/// <summary>
/// Rasterizes a PDF page, or a rectangular part of one, to PNG bytes for the model to look at.
///
/// <para>Sits beside <see cref="ViewportSnapshot"/> because it does the same job from a different
/// source: produce one bounded PNG that becomes an <c>ImageContent</c> block. It shares that
/// class's delivery cap through <see cref="ImageLimits.MaxImageSide"/>, so a picture is the same
/// size whichever of the two made it.</para>
///
/// <para>This is the only part of Physalia backed by a native library. Everything about that —
/// the resolver, the merge exclusion, the platforms it covers — is described on
/// <see cref="PdfNativeLibrary"/>.</para>
/// </summary>
internal static class PdfPageRenderer
{
    /// <summary>
    /// PDFium is not safe to call concurrently. Tool calls within one node are already serialized
    /// by the batch runner, but two Read PDF nodes on a canvas are not serialized with respect to
    /// each other, and neither is a node racing the chat window's own probe of a dropped file.
    /// </summary>
    private static readonly object PdfiumGate = new();

    /// <summary>
    /// Renders one page, or one region of it, as PNG bytes.
    /// </summary>
    /// <param name="path">The absolute path of the PDF.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="region">The part of the page to render.</param>
    /// <param name="dpi">The resolution to render at, applied to the REGION, not the page.</param>
    /// <param name="result">The rendered image on success.</param>
    /// <param name="error">A human-readable reason on failure.</param>
    /// <returns>True when an image was produced.</returns>
    internal static bool TryRender(
        string path,
        int page,
        PdfRegion region,
        int dpi,
        out PdfRenderedPage? result,
        out string? error)
    {
        result = null;

        string? unavailable = PdfNativeLibrary.Install();
        if (unavailable is not null)
        {
            error = unavailable;
            return false;
        }

        if (!File.Exists(path))
        {
            error = $"The file is no longer at {path}. PDFs are referenced where they sit rather " +
                    "than copied, so moving or deleting one after attaching it breaks the link.";
            return false;
        }

        try
        {
            lock (PdfiumGate)
            {
                byte[] bytes = File.ReadAllBytes(path);
                int index = page - 1;

                int pageCount = Conversion.GetPageCount(bytes);
                if (index < 0 || index >= pageCount)
                {
                    error = $"Page {page} does not exist — the document has {pageCount} page(s).";
                    return false;
                }

                SizeF size = Conversion.GetPageSize(bytes, index);
                RectangleF? bounds = null;

                if (!region.IsFullPage)
                {
                    (double left, double top, double width, double height) =
                        region.ToPointsTopLeft(size.Width, size.Height);

                    // PDFium's crop rectangle is measured DOWN from the page top; PdfRegion is the
                    // one place that flip is expressed, so nothing here does its own arithmetic.
                    bounds = new RectangleF(
                        (float)left, (float)top, (float)width, (float)height);
                }

                var options = new RenderOptions(
                    Dpi: dpi,
                    Bounds: bounds,

                    // Load-bearing. Without this, Dpi is applied to the whole PAGE and the cropped
                    // content is stretched across a page-sized canvas — so a tight crop comes back
                    // at the same pixel count as the full sheet, no more readable than before, and
                    // the zoom loop silently accomplishes nothing.
                    DpiRelativeToBounds: bounds is not null,
                    WithAnnotations: true,
                    WithFormFill: true);

                using SKBitmap bitmap = Conversion.ToImage(bytes, page: index, options: options);
                if (bitmap.Width <= 0 || bitmap.Height <= 0)
                {
                    error = "The page rendered to an empty image.";
                    return false;
                }

                result = Encode(bitmap);
                error = null;
                return true;
            }
        }
        catch (DllNotFoundException ex)
        {
            error = "The PDF rendering library could not be loaded: " + ex.Message +
                    " Text extraction still works.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Could not read {Path.GetFileName(path)}: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            // PDFium reports a malformed or password-protected document by throwing, and the type
            // it throws is not part of any contract worth pinning a catch to.
            error = $"Could not render page {page} of {Path.GetFileName(path)}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Encodes a rendered bitmap as PNG, downscaling first when it exceeds the delivery cap.
    /// </summary>
    /// <param name="bitmap">The rendered page.</param>
    /// <returns>The encoded image and its delivered dimensions.</returns>
    private static PdfRenderedPage Encode(SKBitmap bitmap)
    {
        int longest = Math.Max(bitmap.Width, bitmap.Height);
        if (longest <= ImageLimits.MaxImageSide)
        {
            return new PdfRenderedPage(EncodePng(bitmap), bitmap.Width, bitmap.Height, false);
        }

        double scale = (double)ImageLimits.MaxImageSide / longest;
        int width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        int height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        // Mitchell cubic resampling: a drawing downscaled with a cheaper filter loses hairlines
        // and thin dimension text outright, which is exactly the content this is here to preserve.
        using SKBitmap scaled = bitmap.Resize(
            new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType),
            new SKSamplingOptions(SKCubicResampler.Mitchell));

        // Resize returns null when it cannot allocate; sending the oversized original is better
        // than sending nothing, and the provider will resize it anyway.
        return scaled is null
            ? new PdfRenderedPage(EncodePng(bitmap), bitmap.Width, bitmap.Height, false)
            : new PdfRenderedPage(EncodePng(scaled), width, height, true);
    }

    /// <summary>
    /// Encodes a bitmap as PNG bytes.
    /// </summary>
    /// <param name="bitmap">The bitmap to encode.</param>
    /// <returns>The PNG bytes.</returns>
    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
