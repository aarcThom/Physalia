// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Grounding;
using Xunit;

namespace Physalia.Core.Tests.Grounding;

public class DocumentUnitsGroundingTests
{
    [Fact]
    public void ToSystemPromptSection_WithUnits_RendersUnitsLine()
    {
        var grounding = new DocumentUnitsGrounding("Millimeters");

        Assert.Equal(
            "The active Rhino/Grasshopper document uses these units: Millimeters. "
            + "Produce geometry and numeric values consistent with this unit system.",
            grounding.ToSystemPromptSection());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToSystemPromptSection_BlankUnits_ReturnsEmpty(string units)
    {
        Assert.Equal(string.Empty, new DocumentUnitsGrounding(units).ToSystemPromptSection());
    }

    [Fact]
    public void ToSystemPromptSection_TrimsUnits()
    {
        Assert.Contains("units: Inches.", new DocumentUnitsGrounding("  Inches  ").ToSystemPromptSection());
    }
}
