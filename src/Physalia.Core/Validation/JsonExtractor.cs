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
            if ((raw[i] == '{' || raw[i] == '[') && TryScanBalanced(raw, i, out int end))
            {
                candidates.Add(raw.Substring(i, end - i + 1));
                i = end + 1;
            }
            else
            {
                i++;
            }
        }

        return candidates;
    }

    /// <summary>
    /// Scans forward from an opening <c>{</c> or <c>[</c> tracking bracket depth — skipping
    /// string literals and their escapes — and reports where the opener closes.
    /// </summary>
    /// <param name="raw">Raw LLM output string.</param>
    /// <param name="start">Index of the opening brace or bracket.</param>
    /// <param name="end">Receives the index of the matching closer.</param>
    /// <returns>True when the opener closes with matching nesting; false on mismatch or end of input.</returns>
    private static bool TryScanBalanced(string raw, int start, out int end)
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
                        return false;
                    if (closers.Count == 0)
                    {
                        end = i;
                        return true;
                    }

                    break;
            }
        }

        return false;
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
