// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Physalia.Core.Grounding.Components;

/// <summary>
/// An immutable snapshot of the resolvable components in a Grasshopper installation. It is
/// built in the Grasshopper layer (which can read the live component server) and handed to
/// <see cref="ComponentMatcher"/>, so name resolution stays a pure function with no
/// Grasshopper dependency in <c>Physalia.Core</c>.
/// </summary>
public sealed class ComponentCatalog
{
    private IReadOnlyList<CatalogCategory>? _categoryTree;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentCatalog"/> class.
    /// </summary>
    /// <param name="entries">The catalogued component entries.</param>
    public ComponentCatalog(IReadOnlyList<CatalogEntry> entries)
    {
        Entries = entries ?? Array.Empty<CatalogEntry>();
    }

    /// <summary>
    /// Gets the catalogued component entries.
    /// </summary>
    public IReadOnlyList<CatalogEntry> Entries { get; }

    /// <summary>
    /// Gets the number of entries in the catalog.
    /// </summary>
    public int Count => Entries.Count;

    /// <summary>
    /// Gets the distinct component display names, sorted, for prompt grounding.
    /// </summary>
    public IReadOnlyList<string> ComponentNames => Entries
        .Select(e => e.Name)
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// Gets the two-level tree of the catalog's components: each distinct tab (category) with its
    /// distinct, sorted panel (sub-category) names. Blank categories/sub-categories are skipped.
    /// Computed lazily and cached, since the catalog is immutable.
    /// </summary>
    public IReadOnlyList<CatalogCategory> CategoryTree => _categoryTree ??= Entries
        .Where(e => !string.IsNullOrWhiteSpace(e.Category))
        .GroupBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .Select(g => new CatalogCategory(
            g.Key,
            g.Select(e => e.SubCategory)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList()))
        .ToList();

    /// <summary>
    /// Returns a catalog narrowed to the supplied grounding selection. A <see langword="null"/>
    /// selection means "include everything" and returns this same instance. Otherwise the returned
    /// catalog keeps only the entries whose <c>(Category, SubCategory)</c> the selection includes;
    /// selection leaves that match no entry are silently ignored.
    /// </summary>
    /// <param name="selection">The selection to apply, or null to include everything.</param>
    /// <returns>The filtered catalog, or this instance when the selection is null.</returns>
    public ComponentCatalog Filtered(GroundingSelection? selection)
    {
        if (selection is null)
        {
            return this;
        }

        return new ComponentCatalog(Entries
            .Where(e => selection.Includes(e.Category, e.SubCategory))
            .ToList());
    }
}
