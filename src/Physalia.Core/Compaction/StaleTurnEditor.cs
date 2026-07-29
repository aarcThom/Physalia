// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Globalization;

namespace Physalia.Core.Compaction;

/// <summary>
/// Text surgery on assistant turns that have aged out of the working set. Both operations here
/// exist for the same reason: in a generate-place-measure loop the model's own past submissions
/// are the single largest thing in the replayed window, and almost none of that bulk still carries
/// information the model needs.
///
/// <para>Measured on a real session: replayed assistant JSON documents were 495,000 characters of
/// the ~1,000,000-character conversation window — 49% — and 73% of that was documents older than
/// the most recent submission. Those older documents are dead weight twice over: whatever they
/// placed is already reflected in the canvas-state grounding the model reads at the top of every
/// turn, and whatever they failed to place was rejected and superseded.</para>
/// </summary>
public static class StaleTurnEditor
{
    /// <summary>
    /// Replaces a JSON document at the tail of an assistant turn with a one-line stub, keeping any
    /// prose that preceded it. The stub records the document's size so the model can still see that
    /// it submitted something there — a turn that silently loses its document reads as if the model
    /// replied with nothing, which invites it to re-submit.
    ///
    /// <para>Only a document that starts at the beginning of a line is stubbed, matching how the
    /// pipeline's own extractor finds it; inline JSON inside a sentence is left alone.</para>
    /// </summary>
    /// <param name="text">The assistant turn's text.</param>
    /// <returns>The text with its trailing document replaced by a stub, or unchanged when it has none.</returns>
    public static string StubTrailingDocument(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        int start = FindDocumentStart(text!);
        if (start < 0)
        {
            return text!;
        }

        int length = text!.Length - start;

        // Nothing to gain below roughly a line or two of JSON, and the stub itself costs tokens.
        if (length < MinStubbableChars)
        {
            return text;
        }

        string prose = text[..start].TrimEnd();
        string stub = string.Format(
            CultureInfo.InvariantCulture,
            "[a {0:N0}-character {1} document was submitted here and has been elided from this "
            + "transcript — its result is reflected in the canvas state shown to you above; do not "
            + "re-send it]",
            length,
            DescribeKind(text, start));

        return prose.Length == 0 ? stub : prose + "\n" + stub;
    }

    // Roughly two lines of JSON. Below this the stub is as long as what it replaces.
    private const int MinStubbableChars = 240;

    // The first line-initial '{' — the same shape the JSON extractor keys on. Scanning by line
    // start (rather than any '{') keeps prose that merely mentions a brace intact.
    private static int FindDocumentStart(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            bool lineStart = i == 0 || text[i - 1] == '\n';
            if (lineStart && (text[i] == '{' || text[i] == '['))
            {
                return i;
            }
        }

        return -1;
    }

    // Names the document kind from its own "kind" field so the stub says "ghpatch" rather than a
    // generic word. Read from the raw text — this runs on history, where a parse is wasted work.
    private static string DescribeKind(string text, int start)
    {
        int kind = text.IndexOf("\"kind\"", start, StringComparison.Ordinal);
        if (kind < 0)
        {
            return "GhJSON";
        }

        int open = text.IndexOf('"', kind + 6);
        if (open < 0)
        {
            return "GhJSON";
        }

        int close = text.IndexOf('"', open + 1);
        return close < 0 ? "GhJSON" : text[(open + 1)..close];
    }
}
