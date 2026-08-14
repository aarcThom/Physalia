// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Grounding;
using Xunit;

namespace Physalia.Core.Tests.Grounding;

public class ScriptInterfaceGroundingTests
{
    private static ScriptInterfaceGrounding Sample(ScriptInterfaceDialect? dialect = null) => new(
        "Panelize",
        new[]
        {
            new ScriptInterfacePort("pts", "Point", "list"),
            new ScriptInterfacePort("radius", "Number", "item"),
        },
        new[]
        {
            new ScriptInterfacePort("spheres", string.Empty, "list"),
        },
        dialect ?? ScriptInterfaceDialect.Python);

    [Fact]
    public void ToSystemPromptSection_RendersComponentNameAndLockRule()
    {
        string section = Sample().ToSystemPromptSection();

        Assert.Contains("\"Panelize\"", section);
        Assert.Contains("LOCKED interface", section);
        Assert.Contains("Never add, remove, or rename a parameter", section);
    }

    // The lock is language-neutral; what the model is TOLD about it is not. Each dialect must name
    // its own component kind and submission schema, or the model answers with the wrong shape.
    [Fact]
    public void ToSystemPromptSection_PythonDialect_NamesThePythonSchema()
    {
        string section = Sample(ScriptInterfaceDialect.Python).ToSystemPromptSection();

        Assert.Contains("Python script component", section);
        Assert.Contains("PythonComponent JSON", section);
        Assert.DoesNotContain("CSharpComponent", section);
    }

    [Fact]
    public void ToSystemPromptSection_CSharpDialect_NamesTheCSharpSchemaAndTheSignatureRule()
    {
        string section = Sample(ScriptInterfaceDialect.CSharp).ToSystemPromptSection();

        Assert.Contains("C# script component", section);
        Assert.Contains("CSharpComponent JSON", section);
        Assert.Contains("RunScript signature", section);
        Assert.DoesNotContain("PythonComponent", section);
    }

    [Fact]
    public void ToSystemPromptSection_RendersInputsAsJsonEntries()
    {
        string section = Sample().ToSystemPromptSection();

        Assert.Contains("{ \"name\": \"pts\", \"type\": \"Point\", \"access\": \"list\" }", section);
        Assert.Contains("{ \"name\": \"radius\", \"type\": \"Number\", \"access\": \"item\" }", section);
    }

    [Fact]
    public void ToSystemPromptSection_OmitsTypeForUntypedPorts()
    {
        string section = Sample().ToSystemPromptSection();

        Assert.Contains("{ \"name\": \"spheres\", \"access\": \"list\" }", section);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToSystemPromptSection_BlankComponentName_ReturnsEmpty(string name)
    {
        var grounding = new ScriptInterfaceGrounding(
            name,
            System.Array.Empty<ScriptInterfacePort>(),
            System.Array.Empty<ScriptInterfacePort>(),
            ScriptInterfaceDialect.Python);

        Assert.Equal(string.Empty, grounding.ToSystemPromptSection());
    }

    [Fact]
    public void ToSystemPromptSection_EmptyPortLists_RenderAsEmptyArrays()
    {
        var grounding = new ScriptInterfaceGrounding(
            "Bare",
            System.Array.Empty<ScriptInterfacePort>(),
            System.Array.Empty<ScriptInterfacePort>(),
            ScriptInterfaceDialect.Python);
        string section = grounding.ToSystemPromptSection();

        Assert.Contains("\"inputs\": []", section);
        Assert.Contains("\"outputs\": []", section);
    }

    [Fact]
    public void FormatPorts_NullPorts_RendersEmptyArray()
    {
        Assert.Equal("[]", ScriptInterfaceGrounding.FormatPorts(null));
    }

    // ---- Downstream expectations: what the canvas already demands of an output.

    private static ScriptInterfaceGrounding WithDownstream(params string[] types) => new(
        "Panelize",
        System.Array.Empty<ScriptInterfacePort>(),
        new[]
        {
            new ScriptInterfacePort("wall_out", string.Empty, "item") { DownstreamTypes = types },
            new ScriptInterfacePort("spare", string.Empty, "item"),
        },
        ScriptInterfaceDialect.Python);

    [Fact]
    public void ToSystemPromptSection_WiredOutput_StatesTheDownstreamType()
    {
        string section = WithDownstream("Mesh").ToSystemPromptSection();

        Assert.Contains("ALREADY WIRED", section);
        Assert.Contains("wall_out → Mesh", section);
    }

    // The submission schemas set additionalProperties:false on an output entry, so a downstream
    // expectation must never be rendered as a "type" field — a copied entry would fail validation.
    [Fact]
    public void ToSystemPromptSection_WiredOutput_DoesNotAddTypeToTheJsonEntry()
    {
        string section = WithDownstream("Mesh").ToSystemPromptSection();

        Assert.Contains("{ \"name\": \"wall_out\", \"access\": \"item\" }", section);
    }

    [Fact]
    public void ToSystemPromptSection_OutputWiredToSeveralTypes_ListsThemAll()
    {
        string section = WithDownstream("Mesh", "Brep").ToSystemPromptSection();

        Assert.Contains("wall_out → Mesh, Brep", section);
        Assert.Contains("assign something every one of them accepts", section);
    }

    [Fact]
    public void ToSystemPromptSection_UnwiredOutput_IsNotListedAsWired()
    {
        string section = WithDownstream("Mesh").ToSystemPromptSection();

        Assert.DoesNotContain("spare →", section);
    }

    [Fact]
    public void ToSystemPromptSection_NothingWired_OmitsTheDownstreamSectionEntirely()
    {
        string section = Sample().ToSystemPromptSection();

        Assert.DoesNotContain("ALREADY WIRED", section);
        Assert.DoesNotContain("→", section);
    }
}
