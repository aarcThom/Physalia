// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// Pure parser that rewrites "/t/&lt;toolname&gt;" references in a prompt into a clear natural-language
/// mention the model understands. The chat input autocompletes these tokens against the tools currently
/// present in the document (collected by the Tools Present grounder), so by submit time a token names a
/// real, available tool; this normalizes it (e.g. <c>/t/create_rhino_geometry</c> → <c>the
/// "create_rhino_geometry" tool</c>) so the model reaches for the tool the grounding advertised. A "/t/"
/// not followed by a known tool name is left as literal text.
///
/// <para>This mirrors <see cref="PromptClusterResolver"/> exactly, differing only in the marker ("/t/"
/// vs "/c/") and the phrase it produces ("tool" vs "cluster").</para>
/// </summary>
public static class PromptToolResolver
{
    private const string Marker = "/t/";

    /// <summary>
    /// Rewrites each "/t/&lt;name&gt;" token in <paramref name="prompt"/> whose name matches one in
    /// <paramref name="toolNames"/> into the phrase <c>the "&lt;name&gt;" tool</c>. A token is matched
    /// only when the "/t/" sits at a word boundary (start of string or after whitespace) and is followed
    /// by a known tool name ending at a non-word boundary. Longer names win when one is a prefix of
    /// another. Matching is case-insensitive, and the rewrite preserves the tool's canonical casing.
    /// </summary>
    /// <param name="prompt">The raw prompt text.</param>
    /// <param name="toolNames">The names of tools that may be referenced.</param>
    /// <returns>The prompt with every matched tool reference normalized.</returns>
    public static string Normalize(string prompt, IReadOnlyCollection<string> toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        if (string.IsNullOrEmpty(prompt) || toolNames.Count == 0)
        {
            return prompt ?? string.Empty;
        }

        // Longest names first so "/t/create_curve" wins over "/t/create" at the same position.
        var names = new List<string>(toolNames);
        names.RemoveAll(string.IsNullOrWhiteSpace);
        names.Sort((a, b) => b.Length.CompareTo(a.Length));

        var sb = new StringBuilder(prompt.Length);
        int i = 0;
        while (i < prompt.Length)
        {
            if (prompt[i] == '/'
                && (i == 0 || char.IsWhiteSpace(prompt[i - 1]))
                && MatchesMarker(prompt, i))
            {
                string? matched = MatchName(prompt, i + Marker.Length, names);
                if (matched is not null)
                {
                    sb.Append("the \"").Append(matched).Append("\" tool");
                    i += Marker.Length + matched.Length;
                    continue;
                }
            }

            sb.Append(prompt[i]);
            i++;
        }

        return sb.ToString();
    }

    private static bool MatchesMarker(string prompt, int slashIndex) =>
        slashIndex + Marker.Length <= prompt.Length
        && string.Compare(prompt, slashIndex, Marker, 0, Marker.Length, StringComparison.Ordinal) == 0;

    // Returns the known tool name appearing at startIndex (canonical casing), or null. Requires the
    // name to end at a non-word boundary so "/t/foo" does not match inside "/t/foobar".
    private static string? MatchName(string prompt, int startIndex, IReadOnlyList<string> namesLongestFirst)
    {
        foreach (string name in namesLongestFirst)
        {
            int end = startIndex + name.Length;
            if (end > prompt.Length)
            {
                continue;
            }

            if (string.Compare(prompt, startIndex, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }

            if (end < prompt.Length && IsWordChar(prompt[end]))
            {
                continue; // the name is a prefix of a longer word — not a real reference
            }

            return name;
        }

        return null;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_';
}
