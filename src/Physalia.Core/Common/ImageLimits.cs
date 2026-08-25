// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Common;

/// <summary>
/// The bounds every image Physalia sends to a model is held to.
///
/// <para>One definition, shared by everything that produces an image block — the viewport capture
/// behind <c>take_snapshot</c> and Geometry Observation, and the PDF page rasterizer behind
/// <c>read_pdf</c>. They are held together deliberately: two caps that drift apart mean the same
/// picture arrives at two different resolutions depending on which component made it, and the
/// symptom of that is an intermittently unreadable image rather than an obvious bug.</para>
/// </summary>
public static class ImageLimits
{
    /// <summary>
    /// The longest side, in pixels, an image may have when it is handed to a model. Above this it
    /// is downscaled before encoding.
    ///
    /// <para>1568 is the point beyond which the major providers resize server-side anyway, so
    /// sending more costs upload and tokens without adding a readable pixel.</para>
    /// </summary>
    public const int MaxImageSide = 1568;
}
