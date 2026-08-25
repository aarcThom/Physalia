// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Pdf;

/// <summary>
/// One occurrence of a search term on a page, with WHERE on the sheet it sits.
///
/// <para>The location is the point of this type. Text search on a drawing set answers "which sheet"
/// only accidentally; what the model actually needs next is a crop it can look at, and
/// <see cref="Region"/> feeds straight back into a <c>render</c> call. That is the loop the whole
/// tool is built around: search for a callout, render where it landed, read the detail.</para>
/// </summary>
/// <param name="Page">The 1-based page the match sits on.</param>
/// <param name="Text">The matched line, trimmed, for the model to recognise the hit by.</param>
/// <param name="Region">Where the match sits, in normalized top-left-origin page coordinates.</param>
public sealed record PdfSearchHit(int Page, string Text, PdfRegion Region);
