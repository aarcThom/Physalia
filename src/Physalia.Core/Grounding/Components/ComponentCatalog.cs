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
    private HashSet<Guid>? _guids;
    private GroundingSelection? _nativeSelection;

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
    /// Returns whether the catalog contains a component with the given type GUID. Because the
    /// catalog excludes obsolete and hidden components, this doubles as a validity check: an
    /// incoming GUID that is not present is either unknown or points at a deprecated component,
    /// and should be re-resolved by name rather than instantiated as-is.
    /// </summary>
    /// <param name="componentGuid">The component-type GUID to look up.</param>
    /// <returns>True when a catalogued entry has this component GUID.</returns>
    public bool ContainsGuid(Guid componentGuid) =>
        (_guids ??= Entries.Select(e => e.ComponentGuid).ToHashSet()).Contains(componentGuid);

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
    /// Returns the selection covering every leaf that holds at least one native (core-library)
    /// component — the default grounding selection, so plug-in tabs start unchecked and the model
    /// is only offered what Grasshopper ships with until the user opts a plug-in in. A panel mixing
    /// native and plug-in entries is included whole (selection granularity is the leaf). Computed
    /// lazily and cached, since the catalog is immutable.
    /// </summary>
    /// <returns>The native-only default selection.</returns>
    public GroundingSelection NativeSelection() => _nativeSelection ??= GroundingSelection.FromLeaves(
        Entries
            .Where(e => e.IsNative && !string.IsNullOrWhiteSpace(e.Category))
            .Select(e => (e.Category, e.SubCategory ?? string.Empty)));

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
