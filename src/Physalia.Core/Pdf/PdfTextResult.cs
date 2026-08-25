// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;

namespace Physalia.Core.Pdf;

/// <summary>
/// The outcome of a text extraction: what came out, which pages contributed, and — the part that
/// matters most — which pages had nothing to give.
///
/// <para><see cref="EmptyPages"/> exists so the caller never reports an empty string on its own. A
/// scanned drawing and a blank drawing produce identical extraction output, and the model has to be
/// told which it is looking at, or it will conclude the sheet is empty and answer from nothing.</para>
/// </summary>
/// <param name="Text">The extracted text, page-delimited.</param>
/// <param name="IncludedPages">Pages that contributed text.</param>
/// <param name="EmptyPages">Pages that were requested but carry no text layer.</param>
/// <param name="Truncated">Whether the character budget cut the result short.</param>
public sealed record PdfTextResult(
    string Text,
    IReadOnlyList<int> IncludedPages,
    IReadOnlyList<int> EmptyPages,
    bool Truncated);
