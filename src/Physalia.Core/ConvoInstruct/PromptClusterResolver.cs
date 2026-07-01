// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// Pure parser that rewrites "/c/&lt;clustername&gt;" references in a prompt into a clear natural-language
/// mention the model understands. The chat input autocompletes these tokens against the currently
/// included clusters, so by submit time a token names a real, grounded cluster; this normalizes it
/// (e.g. <c>/c/My Cluster</c> → <c>the "My Cluster" cluster</c>) so the model wires the cluster the
/// grounding described. A "/c/" not followed by a known cluster name is left as literal text.
/// </summary>
public static class PromptClusterResolver
{
    private const string Marker = "/c/";

    /// <summary>
    /// Rewrites each "/c/&lt;name&gt;" token in <paramref name="prompt"/> whose name matches one in
    /// <paramref name="clusterNames"/> into the phrase <c>the "&lt;name&gt;" cluster</c>. A token is
    /// matched only when the "/c/" sits at a word boundary (start of string or after whitespace) and is
    /// followed by a known cluster name ending at a non-word boundary. Names may contain spaces (they
    /// are matched against the full known name); longer names win when one is a prefix of another.
    /// Matching is case-insensitive, and the rewrite preserves the cluster's canonical casing.
    /// </summary>
    /// <param name="prompt">The raw prompt text.</param>
    /// <param name="clusterNames">The names of clusters that may be referenced.</param>
    /// <returns>The prompt with every matched cluster reference normalized.</returns>
    public static string Normalize(string prompt, IReadOnlyCollection<string> clusterNames)
    {
        ArgumentNullException.ThrowIfNull(clusterNames);
        if (string.IsNullOrEmpty(prompt) || clusterNames.Count == 0)
        {
            return prompt ?? string.Empty;
        }

        // Longest names first so "/c/Truss Frame" wins over "/c/Truss" at the same position.
        var names = new List<string>(clusterNames);
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
                    sb.Append("the \"").Append(matched).Append("\" cluster");
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

    // Returns the known cluster name appearing at startIndex (canonical casing), or null. Requires the
    // name to end at a non-word boundary so "/c/Truss" does not match inside "/c/Trusses".
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
