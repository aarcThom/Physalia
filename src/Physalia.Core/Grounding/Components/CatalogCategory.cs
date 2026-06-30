// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System.Collections.Generic;

namespace Physalia.Core.Grounding.Components;

/// <summary>
/// One node of the two-level component tree: a Grasshopper tab (<paramref name="Category"/>) and
/// the distinct panel names (<paramref name="SubCategories"/>) that sit under it. Built from a
/// <see cref="ComponentCatalog"/> so a UI can offer a tab → panel selection without depending on
/// the live Grasshopper component server.
/// </summary>
/// <param name="Category">The tab name (e.g. "Kangaroo2").</param>
/// <param name="SubCategories">The distinct, sorted panel names under the tab.</param>
public sealed record CatalogCategory(string Category, IReadOnlyList<string> SubCategories);
