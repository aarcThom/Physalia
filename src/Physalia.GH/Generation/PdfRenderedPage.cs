// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

namespace Physalia.GH.Generation;

/// <summary>
/// One rasterized PDF page, ready to become an image block.
/// </summary>
/// <param name="Png">The encoded image bytes.</param>
/// <param name="Width">The delivered width in pixels.</param>
/// <param name="Height">The delivered height in pixels.</param>
/// <param name="Downscaled">
/// Whether the render was reduced to fit the delivery cap. Reported to the model, because it is the
/// difference between "this detail is not on the drawing" and "this detail did not survive the
/// resize" — and only the second one is fixed by cropping tighter and asking again.
/// </param>
internal sealed record PdfRenderedPage(byte[] Png, int Width, int Height, bool Downscaled);
