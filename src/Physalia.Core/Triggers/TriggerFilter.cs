// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using System.Text.RegularExpressions;

namespace Physalia.Core.Triggers;

/// <summary>
/// Decides whether a changed file is one the user asked to be told about.
///
/// <para>Patterns are the ones people already type into this kind of box — <c>*.las</c>,
/// <c>*.csv;*.txt</c>, <c>site-*.json</c> — matched against the file NAME rather than the whole
/// path, case-insensitively. Blank means everything, which is the default a watcher wants: someone
/// who has not said what they are waiting for is waiting for anything.</para>
///
/// <para>Deliberately NOT a full glob implementation: <c>**</c> and character classes would be a
/// second path-matching language in a plug-in that already has enough of those, and depth is a
/// separate switch on the node (include sub-folders or not) rather than something spelled into the
/// pattern.</para>
/// </summary>
public static class TriggerFilter
{
    // Compiled patterns are cached because a burst of file-system events runs this once per event,
    // and a burst is the normal case (a single copied folder raises one per file).
    private static readonly Dictionary<string, Regex[]> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new();

    /// <summary>
    /// Whether a file name matches the filter.
    /// </summary>
    /// <param name="fileName">The file's name, without its folder.</param>
    /// <param name="filter">Semicolon- or comma-separated patterns; blank or null matches everything.</param>
    /// <returns>True when the file should be reported.</returns>
    public static bool Matches(string? fileName, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        foreach (Regex pattern in Compile(filter))
        {
            if (pattern.IsMatch(fileName))
            {
                return true;
            }
        }

        return false;
    }

    private static Regex[] Compile(string filter)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(filter, out Regex[]? cached))
            {
                return cached;
            }

            var patterns = new List<Regex>();
            foreach (string part in filter.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = part.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                patterns.Add(new Regex(ToRegex(trimmed), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }

            // A filter of nothing but separators is the same as no filter at all: match everything,
            // rather than a pattern set that matches nothing and silently kills the trigger.
            Regex[] result = patterns.Count > 0
                ? patterns.ToArray()
                : new[] { new Regex(".*", RegexOptions.CultureInvariant) };

            Cache[filter] = result;
            return result;
        }
    }

    // Escapes everything, then re-opens the two wildcards. Escaping first is what keeps a pattern
    // like "report(final).*" from being read as a regex group.
    private static string ToRegex(string pattern)
    {
        StringBuilder builder = new("^");

        foreach (char c in pattern)
        {
            builder.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }

        return builder.Append('$').ToString();
    }
}
