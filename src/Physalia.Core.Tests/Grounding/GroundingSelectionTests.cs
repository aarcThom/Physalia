// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Physalia.Core.Grounding;
using Physalia.Core.Grounding.Components;
using Xunit;

namespace Physalia.Core.Tests.Grounding;

public class GroundingSelectionTests
{
    [Fact]
    public void All_FromTree_IncludesEveryLeaf()
    {
        var tree = new List<CatalogCategory>
        {
            new CatalogCategory("Transform", new[] { "Euclidean", "Array" }),
            new CatalogCategory("Kangaroo2", new[] { "Goals" }),
        };

        GroundingSelection selection = GroundingSelection.All(tree);

        Assert.True(selection.Includes("Transform", "Euclidean"));
        Assert.True(selection.Includes("Transform", "Array"));
        Assert.True(selection.Includes("Kangaroo2", "Goals"));
        Assert.False(selection.Includes("Kangaroo2", "Display"));
    }

    [Fact]
    public void Includes_IsCaseInsensitive()
    {
        GroundingSelection selection = GroundingSelection.FromLeaves(new[] { ("Transform", "Euclidean") });

        Assert.True(selection.Includes("transform", "euclidean"));
    }

    [Fact]
    public void Leaves_RoundTripThroughFromLeaves()
    {
        var leaves = new[] { ("Transform", "Euclidean"), ("Kangaroo2", "Goals") };

        GroundingSelection selection = GroundingSelection.FromLeaves(leaves);
        IReadOnlyList<(string Category, string SubCategory)> round = selection.Leaves;

        Assert.Equal(
            leaves.OrderBy(l => l.Item1).ThenBy(l => l.Item2),
            round.OrderBy(l => l.Category).ThenBy(l => l.SubCategory));
    }

    [Fact]
    public void With_AddsAndRemovesLeaf()
    {
        GroundingSelection selection = GroundingSelection.FromLeaves(new[] { ("Transform", "Euclidean") });

        GroundingSelection added = selection.With("Kangaroo2", "Goals", included: true);
        Assert.True(added.Includes("Kangaroo2", "Goals"));
        Assert.True(added.Includes("Transform", "Euclidean"));

        GroundingSelection removed = added.With("Transform", "Euclidean", included: false);
        Assert.False(removed.Includes("Transform", "Euclidean"));
        Assert.True(removed.Includes("Kangaroo2", "Goals"));
    }

    [Fact]
    public void With_IsImmutable()
    {
        GroundingSelection original = GroundingSelection.FromLeaves(new[] { ("Transform", "Euclidean") });

        _ = original.With("Kangaroo2", "Goals", included: true);

        // original is unchanged
        Assert.Single(original.Leaves);
        Assert.False(original.Includes("Kangaroo2", "Goals"));
    }

    [Fact]
    public void FromLeaves_Empty_IncludesNothing()
    {
        GroundingSelection selection = GroundingSelection.FromLeaves(Array.Empty<(string, string)>());

        Assert.Empty(selection.Leaves);
        Assert.False(selection.Includes("Transform", "Euclidean"));
    }
}
