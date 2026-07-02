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
    private static ToolDefinition Tool(string name) => new(name, $"{name} description", "{}");

    [Fact]
    public void ToSystemPromptSection_ListsToolNames_WithOnlyTheseInstruction()
    {
        var grounding = new ToolsGrounding(new List<ToolDefinition>
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
    public void ToSystemPromptSection_NoTools_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, new ToolsGrounding(Array.Empty<ToolDefinition>()).ToSystemPromptSection());
    }
}
