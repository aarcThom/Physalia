// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Text;

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// Pure parser that rewrites the two memory-scope references — <c>/m/global</c> and <c>/m/local</c> —
/// in a prompt into a clear natural-language instruction the model understands. Unlike the tool,
/// cluster, and component resolvers (which normalise a reference to a named on-canvas entity), these
/// are fixed directives the user types to steer where the model records a memory: <c>/m/global</c>
/// targets the shared memory, <c>/m/local</c> the current document's memory. A <c>/m/</c> not followed
/// by one of the two scopes is left as literal text.
///
/// <para>This mirrors <see cref="PromptToolResolver"/>: a marker matched only at a word boundary, the
/// scope ending at a non-word boundary, and the rest of the text preserved verbatim.</para>
/// </summary>
public static class PromptMemoryResolver
{
    private const string Marker = "/m/";

    /// <summary>
    /// Rewrites each <c>/m/global</c> or <c>/m/local</c> token in <paramref name="prompt"/> into an
    /// explicit instruction to save to that memory scope with the memory tool. A token is matched only
    /// when the <c>/m/</c> sits at a word boundary (start of string or after whitespace) and is
    /// followed by <c>global</c> or <c>local</c> ending at a non-word boundary. Matching is
    /// case-insensitive.
    /// </summary>
    /// <param name="prompt">The raw prompt text.</param>
    /// <returns>The prompt with every matched memory reference normalized.</returns>
    public static string Normalize(string prompt)
    {
        if (string.IsNullOrEmpty(prompt))
        {
            return prompt ?? string.Empty;
        }

        var sb = new StringBuilder(prompt.Length);
        int i = 0;
        while (i < prompt.Length)
        {
            if (prompt[i] == '/'
                && (i == 0 || char.IsWhiteSpace(prompt[i - 1]))
                && MatchesMarker(prompt, i))
            {
                string? scope = MatchScope(prompt, i + Marker.Length);
                if (scope is not null)
                {
                    sb.Append(PhraseFor(scope));
                    i += Marker.Length + scope.Length;
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

    private static string? MatchScope(string prompt, int startIndex)
    {
        foreach (string scope in new[] { "global", "local" })
        {
            int end = startIndex + scope.Length;
            if (end > prompt.Length)
            {
                continue;
            }

            if (string.Compare(prompt, startIndex, scope, 0, scope.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }

            if (end < prompt.Length && IsWordChar(prompt[end]))
            {
                continue;
            }

            return scope;
        }

        return null;
    }

    private static string PhraseFor(string scope) => scope.ToLowerInvariant() == "global"
        ? "(save this to your global memory — /memories/global — using the memory tool)"
        : "(save this to this document's local memory — /memories/local — using the memory tool)";

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_';
}
