// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Physalia.Core.Validation;

/// <summary>
/// Pure string-to-string JSON helpers for cleaning up raw LLM output before validation.
/// </summary>
public static class JsonExtractor
{
    /// <summary>
    /// Strips prose and markdown fences from raw LLM output, returning the embedded JSON.
    /// When the output contains several fenced blocks — or, in unfenced text, several balanced
    /// bare JSON spans (a model revising its attempt within one response) — the LAST one that
    /// parses is authoritative, so an unparseable final block (typically truncated output) falls
    /// back to the nearest earlier one that does.
    /// Falls back to returning the trimmed input unchanged if no JSON structure is found.
    /// </summary>
    /// <param name="raw">Raw LLM output string.</param>
    /// <returns>Extracted JSON string.</returns>
    public static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        // Try ```json ... ``` fences, then generic ``` ... ``` fences.
        List<string> blocks = CollectFencedBlocks(raw, "```json");
        if (blocks.Count == 0)
            blocks = CollectFencedBlocks(raw, "```");
        if (blocks.Count > 0)
            return PickAuthoritativeBlock(blocks);

        // No fences: scan for balanced top-level { ... } / [ ... ] candidates. Bare output may
        // contain several JSON attempts with prose between them, or stray braces in the prose
        // itself, so the last candidate that parses wins — the same policy as fenced blocks.
        List<string> candidates = CollectBareJsonCandidates(raw);
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            if (ParsesAsJson(candidates[i]))
                return candidates[i];
        }

        // Nothing parsed: fall back to the outermost { ... } or [ ... ] span, so downstream
        // validation can report what is wrong with it.
        int objStart = raw.IndexOf('{');
        int arrStart = raw.IndexOf('[');

        if (objStart < 0 && arrStart < 0)
            return raw.Trim();

        int start;
        char closing;
        if (objStart >= 0 && (arrStart < 0 || objStart < arrStart))
        {
            start = objStart;
            closing = '}';
        }
        else
        {
            start = arrStart;
            closing = ']';
        }

        int end = raw.LastIndexOf(closing);
        return end > start ? raw.Substring(start, end - start + 1) : raw.Trim();
    }

    /// <summary>
    /// Reports whether the raw output ends inside an unclosed JSON structure — the signature of
    /// a response cut off at the token limit mid-document. When this is true, whatever
    /// <see cref="ExtractJson"/> recovered is an inner fragment of the truncated document, and
    /// validating that fragment produces misleading feedback ("property not allowed at the
    /// document root") — the honest feedback is "your response was cut off".
    /// </summary>
    /// <param name="raw">Raw LLM output string.</param>
    /// <returns>True when an opening brace or bracket never closes before the end of the text.</returns>
    public static bool LooksTruncated(string raw) => FirstDocumentFailure(raw) == ScanOutcome.RanOffEnd;

    /// <summary>
    /// Reports whether the raw output contains a document the model finished writing but got
    /// structurally wrong — brackets that do not pair up, the signature of a single dropped
    /// closing brace somewhere in the middle.
    ///
    /// <para>This case used to be invisible. <see cref="LooksTruncated"/> only fired when a
    /// structure ran off the end of the text, but a brace missing mid-document does not run off
    /// the end: the later closers simply pair with the wrong openers, the scan reports a mismatch,
    /// and the truncation guard stayed silent. What the model got back instead was the schema
    /// verdict on whatever fragment the extractor recovered — "Value is array but should be
    /// object" for a document whose root is plainly an object — and it resubmitted the same shape
    /// twice before recovering. Whatever else validation reports, this has to be said first.</para>
    /// </summary>
    /// <param name="raw">Raw LLM output string.</param>
    /// <returns>True when a document-shaped structure fails to balance.</returns>
    public static bool LooksMalformed(string raw) => FirstDocumentFailure(raw) == ScanOutcome.Mismatched;

    /// <summary>
    /// Scans for the first document-shaped structure that fails to balance and reports how it
    /// failed, or <see cref="ScanOutcome.Balanced"/> when every structure in the text closes
    /// cleanly. Openers that are not document-shaped (a stray brace in prose) are stepped past,
    /// so "…set the {width and height to taste" is not read as a broken document.
    /// </summary>
    /// <param name="raw">Raw LLM output string.</param>
    /// <returns>The failure mode of the first broken document, or Balanced when there is none.</returns>
    private static ScanOutcome FirstDocumentFailure(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ScanOutcome.Balanced;

        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] == '{' || raw[i] == '[')
            {
                ScanOutcome outcome = ScanBalanced(raw, i, out int end);
                if (outcome == ScanOutcome.Balanced)
                {
                    i = end + 1;
                    continue;
                }

                if (IsDocumentOpener(raw, i))
                    return outcome;
            }

            i++;
        }

        return ScanOutcome.Balanced;
    }

    /// <summary>
    /// Collects the contents of every fenced block opened by <paramref name="fence"/> and closed
    /// by the next <c>```</c>, in document order.
    /// </summary>
    /// <param name="raw">Raw LLM output string.</param>
    /// <param name="fence">The opening fence to look for (for example <c>```json</c>).</param>
    /// <returns>The trimmed block contents, oldest first.</returns>
    private static List<string> CollectFencedBlocks(string raw, string fence)
    {
        var blocks = new List<string>();
        int search = 0;

        while (search < raw.Length)
        {
            int fenceStart = raw.IndexOf(fence, search, StringComparison.OrdinalIgnoreCase);
            if (fenceStart < 0) break;

            int newline = raw.IndexOf('\n', fenceStart);
            if (newline < 0) break;

            int fenceEnd = raw.IndexOf("```", newline + 1, StringComparison.Ordinal);
            if (fenceEnd <= newline) break;

            blocks.Add(raw.Substring(newline + 1, fenceEnd - newline - 1).Trim());
            search = fenceEnd + 3;
        }

        return blocks;
    }

    /// <summary>
    /// Collects every balanced top-level <c>{ ... }</c> or <c>[ ... ]</c> span in unfenced text,
    /// in document order. Braces inside JSON string literals do not count toward the balance, and
    /// an opener that never closes is stepped past so a later real object is still found. Prose
    /// spans that happen to balance (for example <c>{width}</c>) are collected too — the caller
    /// filters candidates by whether they parse.
    /// </summary>
    /// <param name="raw">Raw LLM output string.</param>
    /// <returns>The balanced spans, oldest first.</returns>
    private static List<string> CollectBareJsonCandidates(string raw)
    {
        var candidates = new List<string>();
        int i = 0;

        while (i < raw.Length)
        {
            if (raw[i] != '{' && raw[i] != '[')
            {
                i++;
                continue;
            }

            if (ScanBalanced(raw, i, out int end) == ScanOutcome.Balanced)
            {
                candidates.Add(raw.Substring(i, end - i + 1));
                i = end + 1;
                continue;
            }

            // The opener did not close cleanly. Stepping one character forward is right for a
            // stray brace in prose, and catastrophic for a broken DOCUMENT: the scan walks into
            // it and the first inner array that happens to balance — "components" — is collected
            // as if it were the response. The validator then reports that the document root is an
            // array, which is not what the model wrote and not a defect it can act on; observed
            // costing two identical retries on one missing brace. A document-shaped opener is
            // therefore skipped WHOLE, so a fragment of it can never masquerade as the document.
            if (IsDocumentOpener(raw, i))
            {
                i = ApparentExtent(raw, i);
                continue;
            }

            i++;
        }

        return candidates;
    }

    /// <summary>
    /// Whether the opener at <paramref name="index"/> begins something document-shaped — the next
    /// non-whitespace character starts a key, a nested structure, or an array element. Deliberately
    /// tight: prose like <c>{width and height}</c> must not qualify, because the caller skips
    /// whatever this accepts.
    /// </summary>
    /// <param name="raw">Raw LLM output string.</param>
    /// <param name="index">Index of the opening brace or bracket.</param>
    /// <returns>True when the opener looks like the start of a JSON document.</returns>
    private static bool IsDocumentOpener(string raw, int index)
    {
        for (int i = index + 1; i < raw.Length; i++)
        {
            if (!char.IsWhiteSpace(raw[i]))
            {
                return raw[i] is '"' or '{' or '[';
            }
        }

        return false;
    }

    /// <summary>
    /// How far a malformed structure appears to run: forward from the opener counting depth but
    /// tolerating a mismatched closer, to the position just past where depth first returns to
    /// zero. A structure that never gets there ran to the end of the text, so the whole remainder
    /// is one broken document.
    /// </summary>
    /// <param name="raw">Raw LLM output string.</param>
    /// <param name="start">Index of the opening brace or bracket.</param>
    /// <returns>The index to resume scanning from.</returns>
    private static int ApparentExtent(string raw, int start)
    {
        int depth = 0;
        bool inString = false;

        for (int i = start; i < raw.Length; i++)
        {
            char c = raw[i];

            if (inString)
            {
                if (c == '\\')
                    i++;
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                case '[':
                    depth++;
                    break;
                case '}':
                case ']':
                    depth--;
                    if (depth <= 0)
                        return i + 1;
                    break;
            }
        }

        return raw.Length;
    }

    /// <summary>
    /// Scans forward from an opening <c>{</c> or <c>[</c> tracking bracket depth — skipping
    /// string literals and their escapes — and reports where the opener closes.
    /// </summary>
    /// <param name="raw">Raw LLM output string.</param>
    /// <param name="start">Index of the opening brace or bracket.</param>
    /// <param name="end">Receives the index of the matching closer.</param>
    /// <returns>
    /// <see cref="ScanOutcome.Balanced"/> when the opener closes with matching nesting,
    /// <see cref="ScanOutcome.RanOffEnd"/> when the input ended while the structure was still open
    /// (a truncated document rather than a mismatched one), or <see cref="ScanOutcome.Mismatched"/>
    /// when a closer arrives that does not pair with the innermost opener.
    /// </returns>
    private static ScanOutcome ScanBalanced(string raw, int start, out int end)
    {
        end = -1;
        var closers = new Stack<char>();
        bool inString = false;

        for (int i = start; i < raw.Length; i++)
        {
            char c = raw[i];

            if (inString)
            {
                if (c == '\\')
                    i++;
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    closers.Push('}');
                    break;
                case '[':
                    closers.Push(']');
                    break;
                case '}':
                case ']':
                    if (closers.Count == 0 || closers.Pop() != c)
                        return ScanOutcome.Mismatched;
                    if (closers.Count == 0)
                    {
                        end = i;
                        return ScanOutcome.Balanced;
                    }

                    break;
            }
        }

        return closers.Count > 0 ? ScanOutcome.RanOffEnd : ScanOutcome.Mismatched;
    }

    /// <summary>
    /// How a bracket scan ended. The two failure modes are worth telling apart because they mean
    /// different things to the model: <see cref="RanOffEnd"/> is a response cut off at the token
    /// limit, while <see cref="Mismatched"/> is a document the model finished writing but got
    /// wrong — a closer that pairs with the wrong opener, which is what a single missing brace
    /// looks like from the outside.
    /// </summary>
    private enum ScanOutcome
    {
        /// <summary>The opener closed with matching nesting.</summary>
        Balanced,

        /// <summary>The text ended while the structure was still open.</summary>
        RanOffEnd,

        /// <summary>A closer arrived that does not pair with the innermost opener.</summary>
        Mismatched,
    }

    /// <summary>
    /// Picks the block to hand downstream: the last one that parses as JSON, or — when none
    /// parse — the last block outright, so downstream validation produces the correction
    /// feedback instead of this extractor guessing.
    /// </summary>
    /// <param name="blocks">The fenced block contents, oldest first. Must be non-empty.</param>
    /// <returns>The authoritative block.</returns>
    private static string PickAuthoritativeBlock(List<string> blocks)
    {
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            if (ParsesAsJson(blocks[i]))
                return blocks[i];
        }

        return blocks[^1];
    }

    /// <summary>
    /// Reports whether the text parses as JSON.
    /// </summary>
    /// <param name="text">The candidate JSON string.</param>
    /// <returns>True if the text parses; false otherwise.</returns>
    private static bool ParsesAsJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        try
        {
            JsonNode.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Re-serialises a JSON string with indentation. Returns the input unchanged
    /// when it cannot be parsed.
    /// </summary>
    /// <param name="json">The JSON string to format.</param>
    /// <returns>The indented JSON, or the original string on parse failure.</returns>
    public static string PrettyPrint(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? json;
        }
        catch
        {
            return json;
        }
    }
}
