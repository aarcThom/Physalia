// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Physalia.Core.Grounding.Clusters;

/// <summary>
/// An immutable snapshot of the clusters available in the user's <c>Files/CLUSTERS</c> folder. It
/// is built in the Grasshopper layer (which can read and introspect the cluster files) and handed
/// to grounding and placement, so neither needs a Grasshopper dependency in <c>Physalia.Core</c>.
/// </summary>
public sealed class ClusterCatalog
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClusterCatalog"/> class.
    /// </summary>
    /// <param name="entries">The catalogued cluster entries.</param>
    public ClusterCatalog(IReadOnlyList<ClusterEntry> entries)
    {
        Entries = entries ?? Array.Empty<ClusterEntry>();
    }

    /// <summary>
    /// Gets the catalogued cluster entries.
    /// </summary>
    public IReadOnlyList<ClusterEntry> Entries { get; }

    /// <summary>
    /// Gets the number of entries in the catalog.
    /// </summary>
    public int Count => Entries.Count;

    /// <summary>
    /// Gets the distinct cluster display names, sorted, for prompt grounding and autocomplete.
    /// </summary>
    public IReadOnlyList<string> ClusterNames => Entries
        .Select(e => e.Name)
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// Returns the entry whose name matches <paramref name="name"/> (case-insensitive), or null.
    /// </summary>
    /// <param name="name">The cluster name to look up.</param>
    /// <returns>The matching entry, or null when none matches.</returns>
    public ClusterEntry? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns a catalog narrowed to the supplied selection. A <see langword="null"/> selection
    /// means "include everything" and returns this same instance. Otherwise the returned catalog
    /// keeps only the entries whose name the selection includes.
    /// </summary>
    /// <param name="selection">The selection to apply, or null to include everything.</param>
    /// <returns>The filtered catalog, or this instance when the selection is null.</returns>
    public ClusterCatalog Filtered(ClusterSelection? selection)
    {
        if (selection is null)
        {
            return this;
        }

        return new ClusterCatalog(Entries
            .Where(e => selection.Includes(e.Name))
            .ToList());
    }
}
