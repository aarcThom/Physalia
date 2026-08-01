// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Grounding;
using Xunit;

namespace Physalia.Core.Tests.Grounding;

public class ScriptInterfaceGroundingTests
{
    private static ScriptInterfaceGrounding Sample() => new(
        "Panelize",
        new[]
        {
            new ScriptInterfacePort("pts", "Point", "list"),
            new ScriptInterfacePort("radius", "Number", "item"),
        },
        new[]
        {
            new ScriptInterfacePort("spheres", string.Empty, "list"),
        });

    [Fact]
    public void ToSystemPromptSection_RendersComponentNameAndLockRule()
    {
        string section = Sample().ToSystemPromptSection();

        Assert.Contains("\"Panelize\"", section);
        Assert.Contains("LOCKED interface", section);
        Assert.Contains("Never add, remove, or rename a parameter", section);
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
            System.Array.Empty<ScriptInterfacePort>());

        Assert.Equal(string.Empty, grounding.ToSystemPromptSection());
    }

    [Fact]
    public void ToSystemPromptSection_EmptyPortLists_RenderAsEmptyArrays()
    {
        var grounding = new ScriptInterfaceGrounding(
            "Bare",
            System.Array.Empty<ScriptInterfacePort>(),
            System.Array.Empty<ScriptInterfacePort>());
        string section = grounding.ToSystemPromptSection();

        Assert.Contains("\"inputs\": []", section);
        Assert.Contains("\"outputs\": []", section);
    }

    [Fact]
    public void FormatPorts_NullPorts_RendersEmptyArray()
    {
        Assert.Equal("[]", ScriptInterfaceGrounding.FormatPorts(null));
    }
}
