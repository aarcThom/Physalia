// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Physalia.Core.Grounding.Clusters;

/// <summary>
/// An immutable, opt-in selection of which clusters are included when a
/// <see cref="ClusterCatalogGrounding"/> is folded into the system prompt. Keyed by cluster name.
///
/// <para>A <see langword="null"/> selection (handled by the callers, never an instance of this class)
/// means "include everything" — the default for a never-configured Conversation Log. An instance with zero
/// names means "include nothing". Unknown names (referencing a cluster absent from the current
/// folder) are simply never matched, so a selection from another machine degrades gracefully.</para>
/// </summary>
public sealed class ClusterSelection
{
    private readonly HashSet<string> _included;

    private ClusterSelection(HashSet<string> included)
    {
        _included = included;
    }

    /// <summary>
    /// Gets the included cluster names, sorted for stable serialization.
    /// </summary>
    public IReadOnlyList<string> Names => _included
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// Builds a selection from a flat sequence of cluster names. Blank names are dropped.
    /// </summary>
    /// <param name="names">The included cluster names.</param>
    /// <returns>A selection including exactly the supplied names.</returns>
    public static ClusterSelection FromNames(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                set.Add(name);
            }
        }

        return new ClusterSelection(set);
    }

    /// <summary>
    /// Returns whether the given cluster name is included in this selection.
    /// </summary>
    /// <param name="name">The cluster name.</param>
    /// <returns>True when the cluster is included.</returns>
    public bool Includes(string name) =>
        !string.IsNullOrWhiteSpace(name) && _included.Contains(name);
}
