// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.ConvoInstruct;
using Xunit;

namespace Physalia.Core.Tests.ConvoInstruct;

public class PromptClusterResolverTests
{
    private static readonly string[] Names = { "Loft Hull", "Truss", "Truss Frame" };

    [Fact]
    public void Normalize_ReplacesTokenWithCanonicalPhrase()
    {
        string result = PromptClusterResolver.Normalize("Use /c/Truss here", Names);
        Assert.Equal("Use the \"Truss\" cluster here", result);
    }

    [Fact]
    public void Normalize_MatchesMultiWordName()
    {
        string result = PromptClusterResolver.Normalize("build with /c/Loft Hull please", Names);
        Assert.Equal("build with the \"Loft Hull\" cluster please", result);
    }

    [Fact]
    public void Normalize_LongestNameWins()
    {
        string result = PromptClusterResolver.Normalize("/c/Truss Frame now", Names);
        Assert.Equal("the \"Truss Frame\" cluster now", result);
    }

    [Fact]
    public void Normalize_PreservesCanonicalCasing()
    {
        string result = PromptClusterResolver.Normalize("use /c/truss please", Names);
        Assert.Equal("use the \"Truss\" cluster please", result);
    }

    [Fact]
    public void Normalize_IgnoresUnknownClusterName()
    {
        string result = PromptClusterResolver.Normalize("use /c/Unknown please", Names);
        Assert.Equal("use /c/Unknown please", result);
    }

    [Fact]
    public void Normalize_IgnoresMarkerNotAtWordBoundary()
    {
        string result = PromptClusterResolver.Normalize("path/c/Truss", Names);
        Assert.Equal("path/c/Truss", result);
    }

    [Fact]
    public void Normalize_NoNamesOrEmptyPrompt_ReturnsInput()
    {
        Assert.Equal("hi", PromptClusterResolver.Normalize("hi", Array.Empty<string>()));
        Assert.Equal(string.Empty, PromptClusterResolver.Normalize(string.Empty, Names));
    }
}
