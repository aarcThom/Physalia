// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Physalia.Core.Api;

/// <summary>
/// An immutable index of <see cref="ApiMember"/>s with a pure, keyword/symbol search. It is built in
/// the Grasshopper layer (which can reflect over the loaded RhinoCommon assembly and read its XML
/// documentation) and queried here, so retrieval stays a pure function with no Rhino dependency in
/// <c>Physalia.Core</c>.
///
/// <para>Scoring is code-aware rather than purely semantic: an exact member-name hit dominates, a
/// prefix hit beats a substring hit, the declaring type's short name contributes, and a summary hit
/// is a weak tie-breaker. This favours the exact-symbol queries a code-generating model issues
/// (<c>"Brep.CreateFromLoft"</c>, <c>"meshfrombrep"</c>) over fuzzy similarity, which tends to return
/// sibling overloads instead of the asked-for member.</para>
/// </summary>
public sealed class ApiIndex
{
    private static readonly char[] Separators =
    {
        ' ', '\t', '\n', '\r', ',', '.', '(', ')', '<', '>', '/', '-', '[', ']', ';', ':', '{', '}',
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiIndex"/> class.
    /// </summary>
    /// <param name="members">The indexed members.</param>
    public ApiIndex(IReadOnlyList<ApiMember> members)
    {
        Members = members ?? Array.Empty<ApiMember>();
    }

    /// <summary>
    /// Gets the indexed members.
    /// </summary>
    public IReadOnlyList<ApiMember> Members { get; }

    /// <summary>
    /// Gets the number of members in the index.
    /// </summary>
    public int Count => Members.Count;

    /// <summary>
    /// Searches the index for the members best matching a keyword or symbol query, ranked best-first.
    /// </summary>
    /// <param name="query">The search query (keywords, a member name, or a <c>Type.Member</c> phrase).</param>
    /// <param name="maxResults">The maximum number of results; values ≤ 0 default to 20.</param>
    /// <returns>The matching members ordered by descending relevance.</returns>
    public IReadOnlyList<ApiMember> Search(string query, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ApiMember>();
        }

        List<string> tokens = Tokenize(query);
        if (tokens.Count == 0)
        {
            return Array.Empty<ApiMember>();
        }

        int cap = maxResults <= 0 ? 20 : maxResults;

        return Members
            .Select(m => (Member: m, Score: Score(m, tokens)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Member.DeclaringType, StringComparer.Ordinal)
            .ThenBy(x => x.Member.Signature.Length)
            .Take(cap)
            .Select(x => x.Member)
            .ToList();
    }

    private static List<string> Tokenize(string query) =>
        query.ToLowerInvariant()
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static int Score(ApiMember member, IReadOnlyList<string> tokens)
    {
        string name = member.NameLower;
        string typeTail = member.TypeTailLower;
        string summary = member.SummaryLower;

        int score = 0;
        foreach (string token in tokens)
        {
            if (name == token)
            {
                score += 1000;
            }
            else if (name.StartsWith(token, StringComparison.Ordinal))
            {
                score += 200;
            }
            else if (name.Contains(token, StringComparison.Ordinal))
            {
                score += 80;
            }

            if (typeTail == token)
            {
                score += 150;
            }
            else if (typeTail.Contains(token, StringComparison.Ordinal))
            {
                score += 40;
            }

            if (summary.Length > 0 && summary.Contains(token, StringComparison.Ordinal))
            {
                score += 10;
            }
        }

        return score;
    }
}
