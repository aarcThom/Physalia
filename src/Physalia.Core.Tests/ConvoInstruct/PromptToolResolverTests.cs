// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.ConvoInstruct;
using Xunit;

namespace Physalia.Core.Tests.ConvoInstruct;

public class PromptToolResolverTests
{
    private static readonly string[] Names = { "web_search", "create", "create_rhino_geometry" };

    private static readonly string[] WithMemory = { "web_search", "memory" };

    [Fact]
    public void Normalize_MemoryGlobalScope()
    {
        string result = PromptToolResolver.Normalize("note /t/memory/global please", WithMemory);
        Assert.Contains("\"memory\" tool", result);
        Assert.Contains("global memory", result);
        Assert.Contains("/memories/global", result);
        Assert.DoesNotContain("/t/memory", result);
    }

    [Fact]
    public void Normalize_MemoryLocalScope()
    {
        string result = PromptToolResolver.Normalize("use /t/memory/local now", WithMemory);
        Assert.Contains("local memory", result);
        Assert.Contains("/memories/local", result);
        Assert.DoesNotContain("/t/memory", result);
    }

    [Fact]
    public void Normalize_MemoryWithoutScope_IsPlainToolMention()
    {
        string result = PromptToolResolver.Normalize("use /t/memory here", WithMemory);
        Assert.Equal("use the \"memory\" tool here", result);
    }

    [Fact]
    public void Normalize_ScopeSuffixOnlyAppliesToMemory()
    {
        // A "/global" after a non-memory tool is not consumed as a scope.
        string result = PromptToolResolver.Normalize("call /t/web_search/global", WithMemory);
        Assert.Equal("call the \"web_search\" tool/global", result);
    }

    [Fact]
    public void Normalize_ReplacesTokenWithCanonicalPhrase()
    {
        string result = PromptToolResolver.Normalize("Call /t/web_search now", Names);
        Assert.Equal("Call the \"web_search\" tool now", result);
    }

    [Fact]
    public void Normalize_LongestNameWins()
    {
        string result = PromptToolResolver.Normalize("use /t/create_rhino_geometry here", Names);
        Assert.Equal("use the \"create_rhino_geometry\" tool here", result);
    }

    [Fact]
    public void Normalize_PreservesCanonicalCasing()
    {
        string result = PromptToolResolver.Normalize("use /t/WEB_SEARCH please", Names);
        Assert.Equal("use the \"web_search\" tool please", result);
    }

    [Fact]
    public void Normalize_IgnoresUnknownToolName()
    {
        string result = PromptToolResolver.Normalize("use /t/nope please", Names);
        Assert.Equal("use /t/nope please", result);
    }

    [Fact]
    public void Normalize_IgnoresMarkerNotAtWordBoundary()
    {
        string result = PromptToolResolver.Normalize("path/t/create", Names);
        Assert.Equal("path/t/create", result);
    }

    [Fact]
    public void Normalize_NoNamesOrEmptyPrompt_ReturnsInput()
    {
        Assert.Equal("hi", PromptToolResolver.Normalize("hi", Array.Empty<string>()));
        Assert.Equal(string.Empty, PromptToolResolver.Normalize(string.Empty, Names));
    }
}
