// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Physalia.Core.Grounding;
using Physalia.Core.Grounding.Clusters;
using Xunit;

namespace Physalia.Core.Tests.Grounding;

public class ClusterGroundingTests
{
    private static ClusterEntry Entry(string name, string description = "", string[]? inputs = null, string[]? outputs = null)
    {
        ClusterPort Port(string s)
        {
            string[] parts = s.Split(':');
            return new ClusterPort(parts[0], parts.Length > 1 ? parts[1] : string.Empty);
        }

        var ins = new List<ClusterPort>();
        foreach (string s in inputs ?? Array.Empty<string>())
        {
            ins.Add(Port(s));
        }

        var outs = new List<ClusterPort>();
        foreach (string s in outputs ?? Array.Empty<string>())
        {
            outs.Add(Port(s));
        }

        return new ClusterEntry(name, $@"C:\Files\CLUSTERS\{name}.ghcluster", description, ins, outs);
    }

    [Fact]
    public void ToSystemPromptSection_RendersSignatureWithTypesAndDescription()
    {
        var catalog = new ClusterCatalog(new[]
        {
            Entry("Loft Hull", "Lofts sections into a hull.", new[] { "Sections:Curve", "Count:Integer" }, new[] { "Hull:Brep" }),
        });

        string section = new ClusterCatalogGrounding(catalog).ToSystemPromptSection();

        Assert.Equal(
            "These Grasshopper clusters are available — reference one by its exact name (like a component) where it fits:\n"
            + "- Loft Hull(in: Sections:Curve, Count:Integer) -> (out: Hull:Brep): Lofts sections into a hull.",
            section);
    }

    [Fact]
    public void ToSystemPromptSection_OmitsDescriptionWhenBlank()
    {
        var catalog = new ClusterCatalog(new[] { Entry("Twist", inputs: new[] { "Geo:Geometry" }, outputs: new[] { "Out:Geometry" }) });

        string section = new ClusterCatalogGrounding(catalog).ToSystemPromptSection();

        // No description → the line ends at the signature with no trailing ": <description>".
        Assert.EndsWith("- Twist(in: Geo:Geometry) -> (out: Out:Geometry)", section);
    }

    [Fact]
    public void ToSystemPromptSection_EmptyCatalog_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, new ClusterCatalogGrounding(new ClusterCatalog(Array.Empty<ClusterEntry>())).ToSystemPromptSection());
    }

    [Fact]
    public void Filtered_NullSelection_ReturnsSameInstance()
    {
        var catalog = new ClusterCatalog(new[] { Entry("A"), Entry("B") });
        Assert.Same(catalog, catalog.Filtered(null));
    }

    [Fact]
    public void Filtered_KeepsOnlySelectedNames()
    {
        var catalog = new ClusterCatalog(new[] { Entry("A"), Entry("B"), Entry("C") });

        ClusterCatalog filtered = catalog.Filtered(ClusterSelection.FromNames(new[] { "A", "C" }));

        Assert.Equal(2, filtered.Count);
        Assert.NotNull(filtered.Find("a")); // case-insensitive
        Assert.Null(filtered.Find("B"));
    }
}
