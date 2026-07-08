// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Physalia.Core.Grounding;
using Physalia.Core.Grounding.Components;
using Xunit;

namespace Physalia.Core.Tests.Grounding.Components;

public class ComponentGroundingSignatureTests
{
    private static CatalogEntry Entry(string name, string[]? inputs = null, string[]? outputs = null)
    {
        ComponentPort Port(string s)
        {
            bool required = s.EndsWith('*');
            string[] parts = s.TrimEnd('*').Split(':');
            return new ComponentPort(parts[0], parts.Length > 1 ? parts[1] : string.Empty, required);
        }

        return new CatalogEntry(
            name,
            Guid.NewGuid(),
            "Curve",
            "Spline",
            name,
            IsNative: true,
            Inputs: inputs?.Select(Port).ToList(),
            Outputs: outputs?.Select(Port).ToList());
    }

    [Fact]
    public void ToSystemPromptSection_Default_RendersFlatNameList()
    {
        var catalog = new ComponentCatalog(new[]
        {
            Entry("Catenary", new[] { "A:Point", "G:Vector" }, new[] { "C:Curve" }),
            Entry("Loft"),
        });

        string section = new ComponentCatalogGrounding(catalog).ToSystemPromptSection();

        // IncludeSignatures defaults to false: the flat comma list, even when entries carry ports.
        Assert.Equal(
            "These Grasshopper components are installed and available — native and plug-in alike. "
            + "This list is the authoritative catalogue of what may be placed: use these exact names, "
            + "and only components from this list:\n"
            + "Catenary, Loft",
            section);
    }

    [Fact]
    public void ToSystemPromptSection_WithSignatures_RendersTypedLines()
    {
        var catalog = new ComponentCatalog(new[]
        {
            Entry("Catenary", new[] { "A:Point", "B:Point", "L:Number", "G:Vector" }, new[] { "C:Curve" }),
        });

        string section = new ComponentCatalogGrounding(catalog, IncludeSignatures: true).ToSystemPromptSection();

        Assert.Equal(
            "These Grasshopper components are installed and available — native and plug-in alike. "
            + "This list is the authoritative catalogue of what may be placed: use these exact names, "
            + "and only components from this list. Each signature entry shows its input and output "
            + "parameters as Name:Type, listed in paramIndex order — the first parameter is "
            + "paramIndex 0; use these exact Names in inputSettings.parameterName. "
            + "An input marked * is REQUIRED: it has no built-in default, so wire it or "
            + "internalize a value — left empty it produces nulls or nothing downstream. "
            + "Supply data matching these types:\n"
            + "- Catenary(in: A:Point, B:Point, L:Number, G:Vector) -> (out: C:Curve)",
            section);
    }

    [Fact]
    public void ToSystemPromptSection_WithSignatures_MarksRequiredInputs()
    {
        var catalog = new ComponentCatalog(new[]
        {
            Entry("Division", new[] { "A:Number*", "B:Number*" }, new[] { "Result:Number" }),
        });

        string section = new ComponentCatalogGrounding(catalog, IncludeSignatures: true).ToSystemPromptSection();

        // Required inputs (no built-in default) carry a trailing *; outputs never do.
        Assert.Contains("- Division(in: A:Number*, B:Number*) -> (out: Result:Number)", section);
    }

    [Fact]
    public void SignatureFormat_Port_RequiredAppendsAsterisk()
    {
        Assert.Equal("A:Number*", SignatureFormat.Port("A", "Number", required: true));
        Assert.Equal("A:Number", SignatureFormat.Port("A", "Number", required: false));
        Assert.Equal("A*", SignatureFormat.Port("A", string.Empty, required: true));
    }

    [Fact]
    public void ToSystemPromptSection_WithSignatures_NullPortsFallBackToNameOnly()
    {
        var catalog = new ComponentCatalog(new[]
        {
            Entry("Catenary", new[] { "A:Point" }, new[] { "C:Curve" }),
            Entry("Mystery Plugin"),
        });

        string section = new ComponentCatalogGrounding(catalog, IncludeSignatures: true).ToSystemPromptSection();

        // A failed/never-run introspection (null ports) lands in the collapsed names-only
        // paragraph instead of getting a signature line of its own.
        Assert.Contains("- Catenary(in: A:Point) -> (out: C:Curve)", section);
        Assert.Contains("Also installed (names only): Mystery Plugin", section);
        Assert.DoesNotContain("Mystery Plugin(", section);
        Assert.DoesNotContain("- Mystery Plugin", section);
    }

    [Fact]
    public void ToSystemPromptSection_WithSignatures_BlankTypeHintRendersBareName()
    {
        var catalog = new ComponentCatalog(new[]
        {
            Entry("Merge", new[] { "D1:Generic Data", "…" }, new[] { "R:Generic Data" }),
        });

        string section = new ComponentCatalogGrounding(catalog, IncludeSignatures: true).ToSystemPromptSection();

        // The variable-parameter sentinel "…" has no type hint — no dangling colon.
        Assert.Contains("- Merge(in: D1:Generic Data, …) -> (out: R:Generic Data)", section);
    }

    [Fact]
    public void ToSystemPromptSection_WithSignatures_SortsAndDeduplicatesByName()
    {
        var catalog = new ComponentCatalog(new[]
        {
            Entry("Zebra", new[] { "A:Point" }, new[] { "B:Point" }),
            Entry("Apple", new[] { "A:Point" }, new[] { "B:Point" }),
            Entry("apple", new[] { "X:Number" }, new[] { "Y:Number" }),
        });

        string section = new ComponentCatalogGrounding(catalog, IncludeSignatures: true).ToSystemPromptSection();

        string[] lines = section.Split('\n');
        Assert.Equal(3, lines.Length); // header + two entries (case-insensitive dedupe keeps the first)
        Assert.StartsWith("- Apple(", lines[1]);
        Assert.StartsWith("- Zebra(", lines[2]);
    }

    [Fact]
    public void ToSystemPromptSection_EmptyCatalog_ReturnsEmpty()
    {
        var empty = new ComponentCatalog(Array.Empty<CatalogEntry>());
        Assert.Equal(string.Empty, new ComponentCatalogGrounding(empty, IncludeSignatures: true).ToSystemPromptSection());
    }

    [Fact]
    public void SignatureFormat_Port_BlankHintOmitsColon()
    {
        Assert.Equal("G:Vector", SignatureFormat.Port("G", "Vector"));
        Assert.Equal("G", SignatureFormat.Port("G", string.Empty));
        Assert.Equal("G", SignatureFormat.Port("G", "   "));
    }
}
