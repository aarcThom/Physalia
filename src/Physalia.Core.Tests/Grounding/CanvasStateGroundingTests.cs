// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Grounding;
using Xunit;

namespace Physalia.Core.Tests.Grounding;

public class CanvasStateGroundingTests
{
    private const string Json = "{\"schema\":\"1.0\",\"components\":[{\"name\":\"Circle\",\"id\":1}]}";
    private const string Checksum = "sha256-abc123";

    [Fact]
    public void ToSystemPromptSection_WithState_RendersJsonAndChecksum()
    {
        string section = new CanvasStateGrounding(Json, Checksum, 1).ToSystemPromptSection();

        Assert.Contains("CURRENT state of the Grasshopper canvas", section);
        Assert.Contains(Json, section);
        Assert.Contains("patch.base.checksum: " + Checksum, section);
        Assert.Contains("ghpatch", section);
        Assert.Contains("physalia.rhinoRef", section);
    }

    [Fact]
    public void ToSystemPromptSection_EmptyCanvas_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, new CanvasStateGrounding(string.Empty, string.Empty, 0).ToSystemPromptSection());
    }

    [Fact]
    public void ToSystemPromptSection_ZeroComponents_ReturnsEmptyEvenWithText()
    {
        // A zero-count snapshot must contribute nothing, so the model falls back to emitting a
        // full document rather than patching an empty canvas.
        Assert.Equal(string.Empty, new CanvasStateGrounding(Json, Checksum, 0).ToSystemPromptSection());
    }

    [Fact]
    public void ToSystemPromptSection_BlankJson_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, new CanvasStateGrounding("   ", Checksum, 3).ToSystemPromptSection());
    }

    [Fact]
    public void ToSystemPromptSection_BlankChecksum_OmitsChecksumLine()
    {
        string section = new CanvasStateGrounding(Json, string.Empty, 1).ToSystemPromptSection();

        Assert.Contains(Json, section);
        Assert.DoesNotContain("checksum", section, System.StringComparison.OrdinalIgnoreCase);
    }
}
