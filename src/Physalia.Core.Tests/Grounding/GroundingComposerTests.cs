// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Grounding;
using Physalia.Core.Grounding.Clusters;
using Physalia.Core.Grounding.Components;
using Xunit;

namespace Physalia.Core.Tests.Grounding;

public class GroundingComposerTests
{
    private static ComponentCatalog Catalog(params string[] names)
    {
        var entries = new List<CatalogEntry>();
        foreach (string name in names)
        {
            entries.Add(new CatalogEntry(name, Guid.NewGuid(), "Cat", "Sub", "NN", true));
        }

        return new ComponentCatalog(entries);
    }

    [Fact]
    public void Append_CatalogGrounding_MatchesLegacyText()
    {
        var groundings = new List<Core.Grounding.Grounding>
        {
            new ComponentCatalogGrounding(Catalog("Move", "Construct Point")),
        };

        SystemPrompt result = GroundingComposer.Append("BASE PROMPT", groundings);

        // Same shape as the old System Prompt component's catalog append: base, blank line, header line, CSV of sorted names.
        Assert.Equal(
            "BASE PROMPT\n\nThese are the ONLY Grasshopper components you may place — use these exact names:\nConstruct Point, Move",
            result.Text);
    }

    [Fact]
    public void Append_MultipleGroundings_JoinedByBlankLines()
    {
        var groundings = new List<Core.Grounding.Grounding>
        {
            new ComponentCatalogGrounding(Catalog("Move")),
            new PythonFunctionGrounding("def foo(a)", "Does foo."),
        };

        SystemPrompt result = GroundingComposer.Append("BASE", groundings);

        Assert.Equal(
            "BASE\n\nThese are the ONLY Grasshopper components you may place — use these exact names:\nMove\n\nThe following python function is available — use it where it fits:\ndef foo(a)\nDoes foo.",
            result.Text);
    }

    [Fact]
    public void Append_EmptySections_AreDropped()
    {
        var groundings = new List<Core.Grounding.Grounding>
        {
            new ComponentCatalogGrounding(Catalog()),                         // empty catalog -> no section
            new ClusterCatalogGrounding(new ClusterCatalog(Array.Empty<ClusterEntry>())), // empty -> no section
        };

        SystemPrompt result = GroundingComposer.Append("BASE", groundings);

        Assert.Equal("BASE", result.Text);
    }

    [Fact]
    public void Append_NoGroundings_ReturnsPromptUnchanged()
    {
        Assert.Equal("BASE", GroundingComposer.Append("BASE", new List<Core.Grounding.Grounding>()).Text);
    }

    [Fact]
    public void Append_CanvasState_SortsBehindStableSections_WhateverTheWireOrder()
    {
        // Canvas state wired FIRST — the order a user's wiring can easily produce, and the order
        // that would otherwise put a per-turn section inside the cacheable prefix.
        var groundings = new List<Core.Grounding.Grounding>
        {
            new CanvasStateGrounding("{\"schema\":\"1.0\"}", "sha256-abc", ComponentCount: 3),
            new ComponentCatalogGrounding(Catalog("Move")),
            new DocumentUnitsGrounding("Millimeters"),
        };

        SystemPrompt result = GroundingComposer.Append("BASE", groundings);

        Assert.Equal(4, result.Segments.Count);
        Assert.Equal(SystemPromptStability.Volatile, result.Segments[^1].Stability);
        Assert.All(
            result.Segments.Take(result.Segments.Count - 1),
            s => Assert.Equal(SystemPromptStability.Stable, s.Stability));

        // Relative order inside the stable group survives the sort.
        Assert.StartsWith("BASE", result.Text, StringComparison.Ordinal);
        Assert.True(
            result.Text.IndexOf("ONLY Grasshopper components", StringComparison.Ordinal)
            < result.Text.IndexOf("uses these units", StringComparison.Ordinal));

        // The volatile canvas state sits entirely outside the stable prefix.
        Assert.DoesNotContain("sha256-abc", result.Text[..result.StableCharCount], StringComparison.Ordinal);
        Assert.Contains("sha256-abc", result.VolatileSuffix, StringComparison.Ordinal);
    }

    [Fact]
    public void Append_ShortPrompt_TakesNoCacheBreakpoint()
    {
        // Well under the minimum cacheable prefix — a breakpoint here would cost more (cache-write
        // premium) than it saves, and Anthropic rejects prefixes this short outright.
        SystemPrompt result = GroundingComposer.Append("BASE", new List<Core.Grounding.Grounding>());

        Assert.False(result.HasCacheBreakpoint);
    }

    [Fact]
    public void Append_LargeStablePrefix_TakesCacheBreakpointBeforeTheCanvas()
    {
        var groundings = new List<Core.Grounding.Grounding>
        {
            new CanvasStateGrounding("{\"schema\":\"1.0\"}", "sha256-abc", ComponentCount: 3),
        };

        SystemPrompt result = GroundingComposer.Append(new string('x', 8000), groundings);

        Assert.True(result.HasCacheBreakpoint);
        Assert.Equal(new string('x', 8000) + "\n\n", result.StablePrefix);
        Assert.Contains("sha256-abc", result.VolatileSuffix, StringComparison.Ordinal);
        Assert.Equal(result.Text, result.StablePrefix + result.VolatileSuffix);
    }
}
