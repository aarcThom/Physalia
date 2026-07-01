// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Physalia.Core.Grounding.Components;

namespace Physalia.GH.Generation;

/// <summary>
/// Builds a <see cref="ComponentCatalog"/> from the live Grasshopper component server — the single
/// place the installed-component snapshot is assembled. Used both by the Library component
/// (toggle-aware) and by GhJSON placement (default filtering), so both share one filter and
/// de-duplication policy and can never diverge.
/// </summary>
internal static class ComponentCatalogProvider
{
    /// <summary>
    /// Enumerates the component server once and builds the catalog, skipping obsolete entries,
    /// anything without a usable display name, and — unless <paramref name="includeLegacy"/> is set —
    /// hidden-exposure (legacy/back-compat) components that are kept registered but never shown in the
    /// ribbon. Obscure-exposure components (demoted from the ribbon flyout but genuinely useful, e.g.
    /// Mass Multiplication) are kept. Entries are then de-duplicated by display name: when two visible
    /// components share a name, only the more prominent survives (native core-library first, then the
    /// lower exposure value), so a name resolves to a single deterministic target.
    /// </summary>
    /// <param name="includeLegacy">True to keep hidden-exposure legacy components in the catalog.</param>
    /// <returns>The built catalog.</returns>
    internal static ComponentCatalog BuildFromServer(bool includeLegacy = false)
    {
        var server = Instances.ComponentServer;
        var coreLibs = new HashSet<Guid>(server.Libraries.Where(l => l.IsCoreLibrary).Select(l => l.Id));

        // Keyed by display name: keep only the most prominent entry per name.
        var best = new Dictionary<string, (CatalogEntry Entry, bool Native, int Exposure)>(StringComparer.OrdinalIgnoreCase);
        foreach (IGH_ObjectProxy proxy in server.ObjectProxies)
        {
            if (proxy?.Desc is null || proxy.Obsolete)
            {
                continue;
            }

            // Hidden exposure marks a component Grasshopper keeps for back-compat but omits from the
            // ribbon — the "old" twin that collides by name with the current component. Skip by default.
            // (Obsolete-flagged components are already skipped above; obscure ones are kept — demoted
            // but genuinely useful, e.g. Mass Multiplication.)
            if (!includeLegacy && proxy.Exposure == GH_Exposure.hidden)
            {
                continue;
            }

            string name = proxy.Desc.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf("OBSOLETE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            bool native = coreLibs.Contains(proxy.LibraryGuid);
            int exposure = (int)proxy.Exposure;

            if (best.TryGetValue(name, out var current) && !Wins(native, exposure, current))
            {
                continue;
            }

            best[name] = (
                new CatalogEntry(
                    name,
                    proxy.Guid,
                    proxy.Desc.Category ?? string.Empty,
                    proxy.Desc.SubCategory ?? string.Empty,
                    proxy.Desc.NickName ?? string.Empty,
                    native),
                native,
                exposure);
        }

        return new ComponentCatalog(best.Values.Select(v => v.Entry).ToList());
    }

    /// <summary>
    /// Decides whether a name-colliding candidate should displace the current best: native
    /// (core-library) components win, then — among equally-native twins — the lower exposure value
    /// (more prominent placement; an <c>obscure</c> twin always sorts last). Stable on a full tie.
    /// </summary>
    /// <param name="native">Whether the candidate is a native core-library component.</param>
    /// <param name="exposure">The candidate's exposure value (smaller = more prominent).</param>
    /// <param name="current">The current best entry for this name.</param>
    /// <returns>True when the candidate is preferred over the current best.</returns>
    private static bool Wins(bool native, int exposure, (CatalogEntry Entry, bool Native, int Exposure) current)
        => native != current.Native ? native : exposure < current.Exposure;
}
