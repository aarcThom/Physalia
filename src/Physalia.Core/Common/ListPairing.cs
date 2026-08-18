// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Common;

/// <summary>
/// Grasshopper's own list-pairing rule, as a pure function, for the cases where a component reads two
/// lists as WHOLE lists inside one solve and therefore cannot let component-level data matching do it.
/// </summary>
public static class ListPairing
{
    /// <summary>
    /// The entry pairing with an index under LONGEST-LIST matching: equal-length lists pair 1:1, and a
    /// shorter list has its LAST entry reused for every remaining index — which is what Grasshopper
    /// does when it matches a short list against a longer one. An index beyond the end of a longer
    /// list simply returns that list's own entry, so extra entries are ignored rather than an error.
    /// </summary>
    /// <typeparam name="T">The list's element type.</typeparam>
    /// <param name="values">The list being paired; may be empty.</param>
    /// <param name="index">The zero-based index of the item being paired against.</param>
    /// <returns>The paired entry, or the type default when the list is empty or the index is negative.</returns>
    public static T? MatchLongest<T>(IReadOnlyList<T> values, int index)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0 || index < 0)
        {
            return default;
        }

        return values[Math.Min(index, values.Count - 1)];
    }
}
