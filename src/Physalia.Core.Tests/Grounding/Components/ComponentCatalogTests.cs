// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Physalia.Core.Grounding;
using Physalia.Core.Grounding.Components;
using Xunit;

namespace Physalia.Core.Tests.Grounding.Components;

public class ComponentCatalogTests
{
    private static CatalogEntry Entry(string name, string category, string subCategory, bool native = true) =>
        new CatalogEntry(name, Guid.NewGuid(), category, subCategory, string.Empty, native);

    private static ComponentCatalog Catalog(params CatalogEntry[] entries) =>
        new ComponentCatalog(entries);

    [Fact]
    public void CategoryTree_GroupsAndSorts_DistinctTabsAndPanels()
    {
        ComponentCatalog catalog = Catalog(
            Entry("Move", "Transform", "Euclidean"),
            Entry("Rotate", "Transform", "Euclidean"),
            Entry("Grab", "Kangaroo2", "Goals"),
            Entry("Show", "Kangaroo2", "Display"),
            Entry("Blank", "Kangaroo2", "  "));   // blank sub-category dropped

        IReadOnlyList<CatalogCategory> tree = catalog.CategoryTree;

        Assert.Equal(new[] { "Kangaroo2", "Transform" }, tree.Select(c => c.Category));
        Assert.Equal(new[] { "Display", "Goals" }, tree[0].SubCategories);
        Assert.Equal(new[] { "Euclidean" }, tree[1].SubCategories);
    }

    [Fact]
    public void CategoryTree_SkipsBlankCategories()
    {
        ComponentCatalog catalog = Catalog(
            Entry("X", "  ", "Sub"),
            Entry("Y", "Real", "Panel"));

        Assert.Equal(new[] { "Real" }, catalog.CategoryTree.Select(c => c.Category));
    }

    [Fact]
    public void NativeSelection_ChecksOnlyLeavesWithNativeEntries()
    {
        // The default grounding selection: plug-in tabs (Kangaroo2) start unchecked, and a panel
        // holding at least one native entry is included whole, plug-in squatters and all.
        ComponentCatalog catalog = Catalog(
            Entry("Move", "Transform", "Euclidean"),
            Entry("Grab", "Kangaroo2", "Goals", native: false),
            Entry("Plugin Move", "Transform", "Euclidean", native: false));

        GroundingSelection selection = catalog.NativeSelection();

        Assert.True(selection.Includes("Transform", "Euclidean"));
        Assert.False(selection.Includes("Kangaroo2", "Goals"));
        Assert.Equal(new[] { "Move", "Plugin Move" }, catalog.Filtered(selection).Entries.Select(e => e.Name).OrderBy(n => n));
    }

    [Fact]
    public void Filtered_NullSelection_ReturnsSameInstance()
    {
        ComponentCatalog catalog = Catalog(Entry("Move", "Transform", "Euclidean"));

        Assert.Same(catalog, catalog.Filtered(null));
    }

    [Fact]
    public void Filtered_KeepsOnlyIncludedLeaves()
    {
        ComponentCatalog catalog = Catalog(
            Entry("Move", "Transform", "Euclidean"),
            Entry("Grab", "Kangaroo2", "Goals"),
            Entry("Show", "Kangaroo2", "Display"));

        var selection = GroundingSelection.FromLeaves(new[] { ("Kangaroo2", "Goals") });

        ComponentCatalog filtered = catalog.Filtered(selection);

        Assert.Equal(new[] { "Grab" }, filtered.ComponentNames);
    }

    [Fact]
    public void Filtered_SubCategoryNameNotUnique_ScopedByCategory()
    {
        // "Util" appears under two tabs; selecting it under one must not pull in the other.
        ComponentCatalog catalog = Catalog(
            Entry("A", "TabOne", "Util"),
            Entry("B", "TabTwo", "Util"));

        var selection = GroundingSelection.FromLeaves(new[] { ("TabOne", "Util") });

        Assert.Equal(new[] { "A" }, catalog.Filtered(selection).ComponentNames);
    }

    [Fact]
    public void Filtered_UnknownLeaves_IgnoredNoCrash()
    {
        ComponentCatalog catalog = Catalog(Entry("Move", "Transform", "Euclidean"));

        var selection = GroundingSelection.FromLeaves(new[] { ("GhostPlugin", "Vanished") });

        Assert.Empty(catalog.Filtered(selection).Entries);
    }

    [Fact]
    public void Filtered_EmptySelection_ExcludesEverything()
    {
        ComponentCatalog catalog = Catalog(Entry("Move", "Transform", "Euclidean"));

        var selection = GroundingSelection.FromLeaves(Array.Empty<(string, string)>());

        Assert.Empty(catalog.Filtered(selection).Entries);
    }
}
