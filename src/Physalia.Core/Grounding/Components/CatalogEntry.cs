// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;

namespace Physalia.Core.Grounding.Components;

/// <summary>
/// One resolvable Grasshopper component in the installed library: its display name, its
/// component-type GUID, where it sits in the component tabs, and whether it ships with core
/// Grasshopper. The native flag lets the matcher prefer a stock component when several share
/// a display name.
/// </summary>
/// <param name="Name">The component's display name, exactly as Grasshopper reports it.</param>
/// <param name="ComponentGuid">The component-type GUID used to instantiate it.</param>
/// <param name="Category">The component's top-level category (tab).</param>
/// <param name="SubCategory">The component's sub-category (panel).</param>
/// <param name="NickName">The component's nickname, or an empty string.</param>
/// <param name="IsNative">True when the component belongs to a core Grasshopper library.</param>
public sealed record CatalogEntry(
    string Name,
    Guid ComponentGuid,
    string Category,
    string SubCategory,
    string NickName,
    bool IsNative);
