// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Physalia.Core.Grounding.Components;

namespace Physalia.Core.Grounding;

/// <summary>
/// An immutable, opt-in selection of which component-catalog tabs/panels are included when a
/// <see cref="ComponentCatalogGrounding"/> is folded into the system prompt. The selection is keyed
/// <c>Category → set&lt;SubCategory&gt;</c> because panel (sub-category) names are not unique across
/// tabs — a flat sub-category set would wrongly include same-named panels under other tabs.
///
/// <para>A <see langword="null"/> selection (handled by the callers, never an instance of this class)
/// means "include everything" — the default for a never-configured Conversation Log. An instance with zero
/// leaves means "include nothing". Unknown leaves (referencing a tab/panel absent from the current
/// install) are simply never matched, so a selection from another machine degrades gracefully.</para>
/// </summary>
public sealed class GroundingSelection
{
    // Category -> included sub-categories. Ordinal-ignore-case throughout.
    private readonly IReadOnlyDictionary<string, HashSet<string>> _included;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroundingSelection"/> class from a
    /// category → sub-categories map. The map is copied defensively.
    /// </summary>
    /// <param name="included">The included sub-categories grouped by category.</param>
    public GroundingSelection(IReadOnlyDictionary<string, IReadOnlyCollection<string>> included)
    {
        ArgumentNullException.ThrowIfNull(included);

        var copy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, IReadOnlyCollection<string>> pair in included)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            copy[pair.Key] = new HashSet<string>(
                pair.Value ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        _included = copy;
    }

    private GroundingSelection(Dictionary<string, HashSet<string>> included)
    {
        _included = included;
    }

    /// <summary>
    /// Gets the flat list of included <c>(Category, SubCategory)</c> leaves, sorted for stable
    /// serialization.
    /// </summary>
    public IReadOnlyList<(string Category, string SubCategory)> Leaves => _included
        .SelectMany(kvp => kvp.Value.Select(sub => (kvp.Key, sub)))
        .OrderBy(leaf => leaf.Key, StringComparer.OrdinalIgnoreCase)
        .ThenBy(leaf => leaf.Item2, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// Builds a selection that includes every tab and panel in the supplied tree. Used to
    /// materialize a concrete baseline the first time the user narrows the default include-all
    /// selection.
    /// </summary>
    /// <param name="tree">The available category tree.</param>
    /// <returns>A selection including every leaf in the tree.</returns>
    public static GroundingSelection All(IReadOnlyList<CatalogCategory> tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (CatalogCategory node in tree)
        {
            if (string.IsNullOrWhiteSpace(node.Category))
            {
                continue;
            }

            map[node.Category] = new HashSet<string>(
                node.SubCategories ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        return new GroundingSelection(map);
    }

    /// <summary>
    /// Builds a selection from a flat sequence of <c>(Category, SubCategory)</c> leaves.
    /// </summary>
    /// <param name="leaves">The included leaves.</param>
    /// <returns>A selection including exactly the supplied leaves.</returns>
    public static GroundingSelection FromLeaves(IEnumerable<(string Category, string SubCategory)> leaves)
    {
        ArgumentNullException.ThrowIfNull(leaves);

        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach ((string category, string subCategory) in leaves)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                continue;
            }

            if (!map.TryGetValue(category, out HashSet<string>? subs))
            {
                subs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[category] = subs;
            }

            subs.Add(subCategory ?? string.Empty);
        }

        return new GroundingSelection(map);
    }

    /// <summary>
    /// Returns whether the given tab/panel is included in this selection.
    /// </summary>
    /// <param name="category">The tab name.</param>
    /// <param name="subCategory">The panel name.</param>
    /// <returns>True when the leaf is included.</returns>
    public bool Includes(string category, string subCategory)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return false;
        }

        return _included.TryGetValue(category, out HashSet<string>? subs)
            && subs.Contains(subCategory ?? string.Empty);
    }

    /// <summary>
    /// Returns a copy of this selection with one leaf added or removed.
    /// </summary>
    /// <param name="category">The tab name.</param>
    /// <param name="subCategory">The panel name.</param>
    /// <param name="included">True to include the leaf, false to exclude it.</param>
    /// <returns>A new selection reflecting the change.</returns>
    public GroundingSelection With(string category, string subCategory, bool included)
    {
        var leaves = Leaves.ToList();
        leaves.RemoveAll(leaf =>
            string.Equals(leaf.Category, category, StringComparison.OrdinalIgnoreCase)
            && string.Equals(leaf.SubCategory, subCategory, StringComparison.OrdinalIgnoreCase));

        if (included && !string.IsNullOrWhiteSpace(category))
        {
            leaves.Add((category, subCategory ?? string.Empty));
        }

        return FromLeaves(leaves);
    }
}
