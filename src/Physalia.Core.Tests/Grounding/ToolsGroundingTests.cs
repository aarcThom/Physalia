// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Physalia.Core.Common;
using Physalia.Core.Grounding;
using Xunit;

namespace Physalia.Core.Tests.Grounding;

public class ToolsGroundingTests
{
    private static LlmToolDefinition Tool(string name) => new(name, $"{name} description", "{}");

    [Fact]
    public void ToSystemPromptSection_ListsToolNames_WithOnlyTheseInstruction()
    {
        var grounding = new ToolsGrounding(new List<LlmToolDefinition>
        {
            Tool("create_rhino_geometry"),
            Tool("web_search"),
        });

        string section = grounding.ToSystemPromptSection();

        Assert.Contains("Call ONLY these tools", section);
        Assert.Contains("create_rhino_geometry", section);
        Assert.Contains("web_search", section);
    }

    [Fact]
    public void ToSystemPromptSection_AppendsDirectives_AfterTheToolList()
    {
        var grounding = new ToolsGrounding(
            new List<LlmToolDefinition> { Tool("memory") },
            new List<string> { "MEMORY IS NOT OPTIONAL. Read it first." });

        string section = grounding.ToSystemPromptSection();

        Assert.Contains("MEMORY IS NOT OPTIONAL. Read it first.", section);
        Assert.True(
            section.IndexOf("memory", StringComparison.Ordinal)
            < section.IndexOf("MEMORY IS NOT OPTIONAL", StringComparison.Ordinal));
    }

    [Fact]
    public void ToSystemPromptSection_DedupesDirectives_AndDropsBlankOnes()
    {
        var grounding = new ToolsGrounding(
            new List<LlmToolDefinition> { Tool("memory") },
            new List<string> { "Read it first.", "  Read it first.  ", "   ", string.Empty });

        string section = grounding.ToSystemPromptSection();

        Assert.Single(grounding.DirectiveTexts);
        Assert.Equal(
            section.LastIndexOf("Read it first.", StringComparison.Ordinal),
            section.IndexOf("Read it first.", StringComparison.Ordinal));
    }

    [Fact]
    public void ToSystemPromptSection_NoTools_ReturnsEmptyEvenWithADirective()
    {
        // A directive without its tool would order the model to use something it cannot reach.
        var grounding = new ToolsGrounding(
            Array.Empty<LlmToolDefinition>(),
            new List<string> { "MEMORY IS NOT OPTIONAL." });

        Assert.Equal(string.Empty, grounding.ToSystemPromptSection());
    }

    [Fact]
    public void ToSystemPromptSection_NoTools_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, new ToolsGrounding(Array.Empty<LlmToolDefinition>()).ToSystemPromptSection());
    }
}
