// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Physalia.Core.Catalog;

/// <summary>
/// An immutable snapshot of the resolvable components in a Grasshopper installation. It is
/// built in the Grasshopper layer (which can read the live component server) and handed to
/// <see cref="ComponentMatcher"/>, so name resolution stays a pure function with no
/// Grasshopper dependency in <c>Physalia.Core</c>.
/// </summary>
public sealed class ComponentCatalog
{
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
}
