// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;
using Xunit;

namespace Physalia.Core.Tests.ConvoInstruct;

public class PromptMemoryResolverTests
{
    [Fact]
    public void Normalize_RewritesGlobalScope()
    {
        string result = PromptMemoryResolver.Normalize("/m/global the client prefers metric");
        Assert.Contains("global memory", result);
        Assert.Contains("/memories/global", result);
        Assert.Contains("the client prefers metric", result);
        Assert.DoesNotContain("/m/global", result);
    }

    [Fact]
    public void Normalize_RewritesLocalScope()
    {
        string result = PromptMemoryResolver.Normalize("remember: /m/local this document uses feet");
        Assert.Contains("local memory", result);
        Assert.Contains("/memories/local", result);
        Assert.DoesNotContain("/m/local", result);
    }

    [Fact]
    public void Normalize_IsCaseInsensitiveOnScope()
    {
        string result = PromptMemoryResolver.Normalize("/m/GLOBAL note this");
        Assert.Contains("global memory", result);
        Assert.DoesNotContain("/m/GLOBAL", result);
    }

    [Fact]
    public void Normalize_IgnoresUnknownScope()
    {
        Assert.Equal("/m/session note", PromptMemoryResolver.Normalize("/m/session note"));
    }

    [Fact]
    public void Normalize_IgnoresMarkerNotAtWordBoundary()
    {
        Assert.Equal("path/m/global", PromptMemoryResolver.Normalize("path/m/global"));
    }

    [Fact]
    public void Normalize_IgnoresScopeThatIsPrefixOfLongerWord()
    {
        Assert.Equal("/m/globally note", PromptMemoryResolver.Normalize("/m/globally note"));
    }

    [Fact]
    public void Normalize_EmptyPrompt_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PromptMemoryResolver.Normalize(string.Empty));
    }
}
