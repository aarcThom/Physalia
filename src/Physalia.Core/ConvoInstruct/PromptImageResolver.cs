// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;

namespace Physalia.Core.ConvoInstruct;

/// <summary>
/// The result of resolving "/&lt;alias&gt;" image references in a prompt: the interleaved
/// content blocks for the user turn, plus the plain (token-stripped) text for tracing.
/// </summary>
/// <param name="Text">The prompt with all matched reference tokens removed, segments joined by a single space.</param>
/// <param name="Blocks">Ordered content blocks: text segments and images interleaved in reading order.</param>
public sealed record ResolvedPrompt(string Text, IReadOnlyList<MessageContent> Blocks);

/// <summary>
/// Pure parser that turns a prompt containing "/&lt;alias&gt;" references into interleaved
/// text and image content blocks. Aliases are single tokens (no whitespace), matched against
/// a caller-supplied map; a "/" that does not match a known alias is left as literal text.
/// </summary>
public static class PromptImageResolver
{
    /// <summary>
    /// Resolves "/&lt;alias&gt;" tokens in <paramref name="prompt"/> against
    /// <paramref name="imagesByAlias"/>, replacing each matched token in place with an
    /// <see cref="ImageContent"/> block and dropping the token text. A reference is matched
    /// only when the "/" sits at a word boundary (start of string or after whitespace) and is
    /// followed by a known alias that ends at a non-word boundary. Longer aliases win when one
    /// is a prefix of another. Text segments are trimmed and empty segments dropped.
    /// </summary>
    /// <param name="prompt">The raw prompt text.</param>
    /// <param name="imagesByAlias">Map of alias to image source. Keys must not contain whitespace.</param>
    /// <returns>The interleaved content blocks plus the token-stripped plain text.</returns>
    public static ResolvedPrompt Resolve(string prompt, IReadOnlyDictionary<string, ImageSource> imagesByAlias)
    {
        ArgumentNullException.ThrowIfNull(imagesByAlias);
        prompt ??= string.Empty;

        // Longest aliases first so "/photographer" wins over "/photo" at the same position.
        var aliases = new List<string>(imagesByAlias.Keys);
        aliases.Sort((a, b) => b.Length.CompareTo(a.Length));

        var blocks = new List<MessageContent>();
        var textParts = new List<string>();
        var segment = new StringBuilder();

        void FlushSegment()
        {
            string text = segment.ToString().Trim();
            segment.Clear();
            if (text.Length == 0)
            {
                return;
            }

            blocks.Add(new TextContent(text));
            textParts.Add(text);
        }

        int i = 0;
        while (i < prompt.Length)
        {
            if (prompt[i] == '/' && (i == 0 || char.IsWhiteSpace(prompt[i - 1])))
            {
                string? matched = MatchAlias(prompt, i + 1, aliases);
                if (matched is not null)
                {
                    FlushSegment();
                    blocks.Add(new ImageContent(imagesByAlias[matched]));
                    i += 1 + matched.Length;
                    continue;
                }
            }

            segment.Append(prompt[i]);
            i++;
        }

        FlushSegment();

        return new ResolvedPrompt(string.Join(" ", textParts), blocks);
    }

    // Returns the known alias appearing at startIndex, or null. Requires the alias to end at a
    // non-word boundary so "/photo" does not match inside "/photographer".
    private static string? MatchAlias(string prompt, int startIndex, IReadOnlyList<string> aliasesLongestFirst)
    {
        foreach (string alias in aliasesLongestFirst)
        {
            if (alias.Length == 0)
            {
                continue;
            }

            int end = startIndex + alias.Length;
            if (end > prompt.Length)
            {
                continue;
            }

            // Case-insensitive to match the Image Sources' case-insensitive alias uniqueness,
            // so "/Diagram" resolves the alias "diagram".
            if (string.Compare(prompt, startIndex, alias, 0, alias.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }

            if (end < prompt.Length && IsWordChar(prompt[end]))
            {
                // The alias is a prefix of a longer word — not a real reference.
                continue;
            }

            return alias;
        }

        return null;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_';
}
