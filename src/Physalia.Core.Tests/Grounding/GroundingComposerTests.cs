// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Physalia.Core.Grounding;
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

        string result = GroundingComposer.Append("BASE PROMPT", groundings);

        // Equivalent to the old Composer.AppendCatalog: base, blank line, header line, CSV of sorted names.
        Assert.Equal(
            "BASE PROMPT\n\nThese Grasshopper components are installed and available. Use these exact names where one fits:\nConstruct Point, Move",
            result);
    }

    [Fact]
    public void Append_MultipleGroundings_JoinedByBlankLines()
    {
        var groundings = new List<Core.Grounding.Grounding>
        {
            new ComponentCatalogGrounding(Catalog("Move")),
            new PythonFunctionGrounding("def foo(a)", "Does foo."),
        };

        string result = GroundingComposer.Append("BASE", groundings);

        Assert.Equal(
            "BASE\n\nThese Grasshopper components are installed and available. Use these exact names where one fits:\nMove\n\nThe following python function is available — use it where it fits:\ndef foo(a)\nDoes foo.",
            result);
    }

    [Fact]
    public void Append_EmptySections_AreDropped()
    {
        var groundings = new List<Core.Grounding.Grounding>
        {
            new ComponentCatalogGrounding(Catalog()),       // empty catalog -> no section
            new ClusterGrounding(string.Empty, "ignored"),  // no name -> no section
        };

        string result = GroundingComposer.Append("BASE", groundings);

        Assert.Equal("BASE", result);
    }

    [Fact]
    public void Append_NoGroundings_ReturnsPromptUnchanged()
    {
        Assert.Equal("BASE", GroundingComposer.Append("BASE", new List<Core.Grounding.Grounding>()));
    }
}
