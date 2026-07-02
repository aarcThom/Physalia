// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Physalia.Core.Grounding;
using Xunit;

namespace Physalia.Core.Tests.Grounding;

public class CanvasInputGroundingTests
{
    [Fact]
    public void ToSystemPromptSection_ListsInputsWithType_SortedByName()
    {
        var grounding = new CanvasInputGrounding(new List<CanvasInput>
        {
            new("baseCurve", "Curve"),
            new("anchorPt", "Point"),
        });

        string section = grounding.ToSystemPromptSection();

        Assert.Contains("- anchorPt (Point)", section);
        Assert.Contains("- baseCurve (Curve)", section);
        // Sorted case-insensitively: anchorPt before baseCurve.
        Assert.True(section.IndexOf("anchorPt", StringComparison.Ordinal) < section.IndexOf("baseCurve", StringComparison.Ordinal));
    }

    [Fact]
    public void ToSystemPromptSection_NoInputs_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, new CanvasInputGrounding(Array.Empty<CanvasInput>()).ToSystemPromptSection());
    }

    [Fact]
    public void ToSystemPromptSection_SkipsBlankNames()
    {
        var grounding = new CanvasInputGrounding(new List<CanvasInput> { new("   ", "Curve") });
        Assert.Equal(string.Empty, grounding.ToSystemPromptSection());
    }

    [Fact]
    public void ToSystemPromptSection_MissingType_OmitsParens()
    {
        var grounding = new CanvasInputGrounding(new List<CanvasInput> { new("thing", "") });
        Assert.Contains("- thing", grounding.ToSystemPromptSection());
        Assert.DoesNotContain("thing (", grounding.ToSystemPromptSection());
    }
}
