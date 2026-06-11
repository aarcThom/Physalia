// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
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
    /// Falls back to returning the trimmed input unchanged if no JSON structure is found.
    /// </summary>
    /// <param name="raw">Raw LLM output string.</param>
    /// <returns>Extracted JSON string.</returns>
    public static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        // Try ```json ... ``` fence.
        int fenceStart = raw.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fenceStart >= 0)
        {
            int newline = raw.IndexOf('\n', fenceStart);
            if (newline >= 0)
            {
                int fenceEnd = raw.IndexOf("```", newline + 1, StringComparison.Ordinal);
                if (fenceEnd > newline)
                    return raw.Substring(newline + 1, fenceEnd - newline - 1).Trim();
            }
        }

        // Try generic ``` ... ``` fence.
        fenceStart = raw.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            int newline = raw.IndexOf('\n', fenceStart);
            if (newline >= 0)
            {
                int fenceEnd = raw.IndexOf("```", newline + 1, StringComparison.Ordinal);
                if (fenceEnd > newline)
                    return raw.Substring(newline + 1, fenceEnd - newline - 1).Trim();
            }
        }

        // Find outermost { ... } or [ ... ].
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
