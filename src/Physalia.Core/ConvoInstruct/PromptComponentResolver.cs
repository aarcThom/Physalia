// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// Pure parser that rewrites "/c/&lt;tab&gt;/&lt;component&gt;" references in a prompt into a clear
/// natural-language mention the model understands (e.g. <c>/c/Maths/Multiplication</c> →
/// <c>the "Multiplication" component</c>). The chat input autocompletes the token in stages (tab, then
/// component) against the grounded component catalog, so by submit time it names a real component; this
/// normalizes it so the model reaches for that exact component. The <c>&lt;tab&gt;</c> segment is
/// navigational only — it is skipped here; resolution matches the component name (case-insensitive,
/// longest-first) against the component names the caller supplies. A token that does not resolve is left as
/// literal text.
///
/// <para>Only the exact marker "/c/" triggers this — "/cl/" (clusters) and "/t/" (tools) are matched
/// by their own resolvers and are never mistaken for a component reference.</para>
/// </summary>
public static class PromptComponentResolver
{
    private const string Marker = "/c/";

    /// <summary>
    /// Rewrites each "/c/&lt;tab&gt;/&lt;component&gt;" token in <paramref name="prompt"/> whose
    /// component name matches one in <paramref name="componentNames"/> into the phrase
    /// <c>the "&lt;component&gt;" component</c>. A token is matched only when the "/c/" sits at a word
    /// boundary (start of string or after whitespace), is followed by a non-empty tab segment (no
    /// newline) up to the next "/", and then by a known component name ending at a non-word boundary.
    /// Matching is case-insensitive; the rewrite preserves the component's canonical casing.
    /// </summary>
    /// <param name="prompt">The raw prompt text.</param>
    /// <param name="componentNames">The names of components that may be referenced.</param>
    /// <returns>The prompt with every matched component reference normalized.</returns>
    public static string Normalize(string prompt, IReadOnlyCollection<string> componentNames)
    {
        ArgumentNullException.ThrowIfNull(componentNames);
        if (string.IsNullOrEmpty(prompt) || componentNames.Count == 0)
        {
            return prompt ?? string.Empty;
        }

        // Longest names first so "Construct Point" wins over a shorter prefix at the same position.
        var names = componentNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        names.Sort((a, b) => b.Length.CompareTo(a.Length));

        var sb = new StringBuilder(prompt.Length);
        int i = 0;
        while (i < prompt.Length)
        {
            if (prompt[i] == '/'
                && (i == 0 || char.IsWhiteSpace(prompt[i - 1]))
                && MatchesMarker(prompt, i))
            {
                int tabStart = i + Marker.Length;
                int tabSlash = TabSlashIndex(prompt, tabStart);
                if (tabSlash > tabStart)
                {
                    int compStart = tabSlash + 1;
                    string? matched = MatchName(prompt, compStart, names);
                    if (matched is not null)
                    {
                        sb.Append("the \"").Append(matched).Append("\" component");
                        i = compStart + matched.Length;
                        continue;
                    }
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

    // The index of the "/" separating the tab from the component, or -1. The tab segment runs from
    // tabStart up to that "/" and must not cross a newline (a reference is a single run of text).
    private static int TabSlashIndex(string prompt, int tabStart)
    {
        for (int j = tabStart; j < prompt.Length; j++)
        {
            char c = prompt[j];
            if (c == '/')
            {
                return j;
            }

            if (c == '\n' || c == '\r')
            {
                return -1;
            }
        }

        return -1;
    }

    // Returns the known component name appearing at startIndex (canonical casing), or null. Requires the
    // name to end at a non-word boundary so "Point" does not match inside "Point List".
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
                continue;
            }

            return name;
        }

        return null;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_';
}
