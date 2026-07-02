// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.ConvoInstruct;
using Xunit;

namespace Physalia.Core.Tests.ConvoInstruct;

public class PromptComponentResolverTests
{
    private static readonly string[] Names = { "Multiplication", "Point", "Construct Point" };

    [Fact]
    public void Normalize_ReplacesTabbedTokenWithCanonicalPhrase()
    {
        string result = PromptComponentResolver.Normalize("use /c/Maths/Multiplication here", Names);
        Assert.Equal("use the \"Multiplication\" component here", result);
    }

    [Fact]
    public void Normalize_MatchesMultiWordComponent()
    {
        string result = PromptComponentResolver.Normalize("add a /c/Vector/Construct Point please", Names);
        Assert.Equal("add a the \"Construct Point\" component please", result);
    }

    [Fact]
    public void Normalize_PreservesCanonicalCasing()
    {
        string result = PromptComponentResolver.Normalize("/c/Maths/multiplication now", Names);
        Assert.Equal("the \"Multiplication\" component now", result);
    }

    [Fact]
    public void Normalize_IgnoresUnknownComponent()
    {
        string result = PromptComponentResolver.Normalize("use /c/Maths/Nope please", Names);
        Assert.Equal("use /c/Maths/Nope please", result);
    }

    [Fact]
    public void Normalize_IgnoresWithoutTabSegment()
    {
        // No second slash — not a complete component reference.
        string result = PromptComponentResolver.Normalize("use /c/Point please", Names);
        Assert.Equal("use /c/Point please", result);
    }

    [Fact]
    public void Normalize_IgnoresMarkerNotAtWordBoundary()
    {
        string result = PromptComponentResolver.Normalize("path/c/Maths/Point", Names);
        Assert.Equal("path/c/Maths/Point", result);
    }

    [Fact]
    public void Normalize_DoesNotMatchClusterOrToolMarkers()
    {
        // "/cl/" and "/t/" are other resolvers' markers — never a component reference.
        Assert.Equal("/cl/Maths/Point", PromptComponentResolver.Normalize("/cl/Maths/Point", Names));
        Assert.Equal("/t/Maths/Point", PromptComponentResolver.Normalize("/t/Maths/Point", Names));
    }

    [Fact]
    public void Normalize_NoNamesOrEmptyPrompt_ReturnsInput()
    {
        Assert.Equal("hi", PromptComponentResolver.Normalize("hi", Array.Empty<string>()));
        Assert.Equal(string.Empty, PromptComponentResolver.Normalize(string.Empty, Names));
    }
}
