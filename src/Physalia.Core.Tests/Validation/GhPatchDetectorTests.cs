// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Validation;
using Xunit;

namespace Physalia.Core.Tests.Validation;

public class GhPatchDetectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsGhPatch_BlankInput_ReturnsFalse(string? text)
    {
        Assert.False(GhPatchDetector.IsGhPatch(text!));
    }

    [Fact]
    public void IsGhPatch_PatchDocument_ReturnsTrue()
    {
        Assert.True(GhPatchDetector.IsGhPatch(
            "{\"schema\":\"1.0\",\"kind\":\"ghpatch\",\"patch\":{\"components\":{}}}"));
    }

    [Fact]
    public void IsGhPatch_FullGhJsonDocument_ReturnsFalse()
    {
        Assert.False(GhPatchDetector.IsGhPatch(
            "{\"schema\":\"1.0\",\"components\":[{\"name\":\"Circle\",\"id\":1,\"pivot\":\"0,0\"}]}"));
    }

    [Fact]
    public void IsGhPatch_OtherKind_ReturnsFalse()
    {
        Assert.False(GhPatchDetector.IsGhPatch("{\"kind\":\"something-else\"}"));
    }

    [Fact]
    public void IsGhPatch_KindNestedDeeper_ReturnsFalse()
    {
        // Only the TOP-LEVEL kind discriminates; a nested occurrence is data, not a declaration.
        Assert.False(GhPatchDetector.IsGhPatch("{\"schema\":\"1.0\",\"components\":[{\"kind\":\"ghpatch\"}]}"));
    }

    [Fact]
    public void IsGhPatch_TruncatedPatch_ReturnsTrue()
    {
        // Malformed output that still declares the discriminator routes to the patch path, where
        // real parsing produces correction-loop feedback.
        Assert.True(GhPatchDetector.IsGhPatch("{\"schema\":\"1.0\",\"kind\":\"ghpatch\",\"patch\":{\"components\":{\"add\":[{"));
    }

    [Fact]
    public void IsGhPatch_ProseWrappedPatch_AfterExtraction_ReturnsTrue()
    {
        string wrapped = "Here is the patch:\n```json\n{\"kind\":\"ghpatch\",\"patch\":{}}\n```";
        string extracted = JsonExtractor.ExtractJson(wrapped);

        Assert.True(GhPatchDetector.IsGhPatch(extracted));
    }

    [Fact]
    public void IsGhPatch_ArrayInput_ReturnsFalse()
    {
        Assert.False(GhPatchDetector.IsGhPatch("[{\"kind\":\"ghpatch\"}]"));
    }
}
