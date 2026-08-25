// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;

namespace Physalia.Core.Pdf;

/// <summary>
/// What Physalia knows about one PDF without having read its content: where it lives, the short
/// alias the model addresses it by, and a per-page summary. This is the whole of what a freshly
/// attached PDF contributes to a conversation — the descriptor is rendered into one compact block
/// of text in the user turn, and everything beyond it costs a deliberate <c>read_pdf</c> call.
/// A forty-sheet drawing set therefore costs tens of tokens until somebody asks a question about it.
/// </summary>
/// <param name="Path">The absolute path of the file on disk. PDFs are referenced in place, never copied.</param>
/// <param name="Alias">The short, sanitized handle the model passes back as <c>alias</c>.</param>
/// <param name="DisplayName">The original file name, shown to the human and reported to the model.</param>
/// <param name="Pages">One entry per page, in page order.</param>
public sealed record PdfDescriptor(
    string Path,
    string Alias,
    string DisplayName,
    IReadOnlyList<PdfPageInfo> Pages)
{
    /// <summary>
    /// Gets the number of pages in the document.
    /// </summary>
    public int PageCount => Pages.Count;

    /// <summary>
    /// Gets the number of pages that carry an extractable text layer.
    /// </summary>
    public int TextPageCount => Pages.Count(p => p.HasTextLayer);

    /// <summary>
    /// Gets a value indicating whether no page in the document carries a text layer, which is the
    /// signature of a scanned document. Callers report this rather than returning empty text, so
    /// the model reads it as "this must be looked at" instead of "this document is blank".
    /// </summary>
    public bool IsFullyScanned => Pages.Count > 0 && TextPageCount == 0;
}
