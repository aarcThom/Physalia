// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Physalia.Core.Grounding.Components;

/// <summary>
/// Resolves a free-text component name (as an LLM might write it) to a real installed
/// component, tolerating near-misses. Pure: it reasons only over the supplied
/// <see cref="ComponentCatalog"/> and has no Grasshopper dependency.
///
/// <para>The match runs in stages of decreasing certainty — exact name, whitespace- and
/// case-normalised name, nickname, then a fuzzy score (edit-distance ratio, token-sorted
/// ratio, and substring coverage). The best entry is returned with a confidence flag so the
/// caller can decide whether to apply or report it; stock (core-library) components win ties.</para>
/// </summary>
public static class ComponentMatcher
{
    /// <summary>
    /// The minimum fuzzy score (0–100) at which a non-exact match is treated as confident.
    /// </summary>
    public const int ConfidenceThreshold = 85;

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// The outcome of a match attempt.
    /// </summary>
    /// <param name="Entry">The best candidate, or null when the catalog is empty.</param>
    /// <param name="Score">The match score 0–100 (100 = exact).</param>
    /// <param name="IsConfident">Whether the score clears <see cref="ConfidenceThreshold"/>.</param>
    public sealed record MatchResult(CatalogEntry? Entry, int Score, bool IsConfident);

    /// <summary>
    /// Finds the best catalog entry for <paramref name="query"/>.
    /// </summary>
    /// <param name="query">The component name to resolve.</param>
    /// <param name="catalog">The installed-component catalog to resolve against.</param>
    /// <returns>The best candidate with its score and confidence.</returns>
    public static MatchResult Match(string query, ComponentCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(query) || catalog.Count == 0)
        {
            return new MatchResult(null, 0, false);
        }

        string qNorm = Normalize(query);
        string qStripped = Strip(query);

        CatalogEntry? best = null;
        int bestScore = -1;

        foreach (CatalogEntry e in catalog.Entries)
        {
            int score = ScoreEntry(qNorm, qStripped, e);

            if (score > bestScore || (score == bestScore && best is not null && Prefer(e, best)))
            {
                bestScore = score;
                best = e;

                if (score >= 100 && e.IsNative)
                {
                    break;
                }
            }
        }

        bool confident = best is not null && bestScore >= ConfidenceThreshold;
        return new MatchResult(best, Math.Max(bestScore, 0), confident);
    }

    /// <summary>
    /// Scores a single entry against the normalised query.
    /// </summary>
    /// <param name="qNorm">The whitespace- and case-normalised query.</param>
    /// <param name="qStripped">The whitespace-free, lowercased query.</param>
    /// <param name="e">The catalog entry to score.</param>
    /// <returns>A score 0–100.</returns>
    private static int ScoreEntry(string qNorm, string qStripped, CatalogEntry e)
    {
        string name = Normalize(e.Name);
        if (name == qNorm || Strip(e.Name) == qStripped)
        {
            return 100;
        }

        if (!string.IsNullOrEmpty(e.NickName) && e.NickName.Length > 2 && Normalize(e.NickName) == qNorm)
        {
            return 96;
        }

        int score = Math.Max(Ratio(qNorm, name), TokenSortRatio(qNorm, name));

        // Reward clear containment in either direction (e.g. "construct pt" within "construct point").
        if (name.Length > 0 && (name.Contains(qNorm, StringComparison.Ordinal) || qNorm.Contains(name, StringComparison.Ordinal)))
        {
            score = Math.Max(score, 88);
        }

        return score;
    }

    /// <summary>
    /// Decides whether a tying candidate should displace the current best: stock components
    /// win, then the shorter (tighter) name.
    /// </summary>
    /// <param name="candidate">The tying candidate.</param>
    /// <param name="current">The current best.</param>
    /// <returns>True when the candidate is preferred.</returns>
    private static bool Prefer(CatalogEntry candidate, CatalogEntry current)
    {
        if (candidate.IsNative != current.IsNative)
        {
            return candidate.IsNative;
        }

        return candidate.Name.Length < current.Name.Length;
    }

    /// <summary>
    /// Edit-distance similarity of two strings, expressed 0–100.
    /// </summary>
    /// <param name="a">The first string.</param>
    /// <param name="b">The second string.</param>
    /// <returns>100 for identical strings, falling toward 0 as they diverge.</returns>
    private static int Ratio(string a, string b)
    {
        int max = Math.Max(a.Length, b.Length);
        if (max == 0)
        {
            return 100;
        }

        int dist = Levenshtein(a, b);
        return (int)Math.Round(100.0 * (1.0 - ((double)dist / max)));
    }

    /// <summary>
    /// Edit-distance similarity after sorting each string's whitespace-separated tokens, so
    /// word order does not matter.
    /// </summary>
    /// <param name="a">The first string.</param>
    /// <param name="b">The second string.</param>
    /// <returns>The token-sorted ratio 0–100.</returns>
    private static int TokenSortRatio(string a, string b) => Ratio(SortTokens(a), SortTokens(b));

    /// <summary>
    /// Returns the string's tokens sorted and rejoined with single spaces.
    /// </summary>
    /// <param name="s">The string to reorder.</param>
    /// <returns>The token-sorted string.</returns>
    private static string SortTokens(string s)
    {
        string[] tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Array.Sort(tokens, StringComparer.Ordinal);
        return string.Join(" ", tokens);
    }

    /// <summary>
    /// Trims, lowercases, and collapses internal whitespace runs to single spaces.
    /// </summary>
    /// <param name="s">The string to normalise.</param>
    /// <returns>The normalised string.</returns>
    private static string Normalize(string s) => WhitespaceRun.Replace(s.Trim().ToLowerInvariant(), " ");

    /// <summary>
    /// Lowercases and removes all whitespace.
    /// </summary>
    /// <param name="s">The string to strip.</param>
    /// <returns>The whitespace-free, lowercased string.</returns>
    private static string Strip(string s) => WhitespaceRun.Replace(s.ToLowerInvariant(), string.Empty);

    /// <summary>
    /// Computes the Levenshtein edit distance between two strings.
    /// </summary>
    /// <param name="a">The first string.</param>
    /// <param name="b">The second string.</param>
    /// <returns>The minimum single-character edits to turn one into the other.</returns>
    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        int[] prev = Enumerable.Range(0, b.Length + 1).ToArray();
        int[] curr = new int[b.Length + 1];

        for (int i = 0; i < a.Length; i++)
        {
            curr[0] = i + 1;
            for (int j = 0; j < b.Length; j++)
            {
                int cost = a[i] == b[j] ? 0 : 1;
                curr[j + 1] = Math.Min(Math.Min(curr[j] + 1, prev[j + 1] + 1), prev[j] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}
