// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Physalia.Core.ConvoInstruct;
using Xunit;

namespace Physalia.Core.Tests.ConvoInstruct;

public class SystemPromptTests
{
    private static SystemPromptSegment Stable(string text) => new(text, SystemPromptStability.Stable);

    private static SystemPromptSegment Volatile(string text) => new(text, SystemPromptStability.Volatile);

    [Fact]
    public void Construction_SortsStableAheadOfVolatile()
    {
        var prompt = new SystemPrompt(new[] { Volatile("CANVAS"), Stable("PREAMBLE"), Volatile("MORE"), Stable("CATALOG") });

        Assert.Equal(
            new[] { "PREAMBLE", "CATALOG", "CANVAS", "MORE" },
            prompt.Segments.Select(s => s.Text));
    }

    [Fact]
    public void Construction_PreservesRelativeOrderWithinEachGroup()
    {
        var prompt = new SystemPrompt(new[] { Stable("A"), Stable("B"), Stable("C") });

        Assert.Equal("A\n\nB\n\nC", prompt.Text);
    }

    [Fact]
    public void Construction_DropsEmptyAndWhitespaceSegments()
    {
        var prompt = new SystemPrompt(new[] { Stable("A"), Stable("   "), Stable(string.Empty), Stable("B") });

        Assert.Equal(2, prompt.Segments.Count);
        Assert.Equal("A\n\nB", prompt.Text);
    }

    [Fact]
    public void StablePrefixAndVolatileSuffix_PartitionTheTextExactly()
    {
        var prompt = new SystemPrompt(new[] { Stable(new string('s', 5000)), Volatile("CANVAS") });

        Assert.Equal(prompt.Text, prompt.StablePrefix + prompt.VolatileSuffix);
        Assert.EndsWith("\n\n", prompt.StablePrefix, System.StringComparison.Ordinal);
        Assert.Equal("CANVAS", prompt.VolatileSuffix);
    }

    [Fact]
    public void HasCacheBreakpoint_FalseBelowTheMinimum()
    {
        // A prefix this short costs more to write to cache than it can ever save.
        var prompt = new SystemPrompt(new[] { Stable("short"), Volatile("CANVAS") });

        Assert.False(prompt.HasCacheBreakpoint);
    }

    [Fact]
    public void HasCacheBreakpoint_TrueForAWhollyStablePrompt()
    {
        // Nothing volatile still deserves a breakpoint: the whole prompt caches, and the
        // conversation that follows it is what varies.
        var prompt = new SystemPrompt(new[] { Stable(new string('s', 5000)) });

        Assert.True(prompt.HasCacheBreakpoint);
        Assert.Equal(prompt.Text, prompt.StablePrefix);
        Assert.Equal(string.Empty, prompt.VolatileSuffix);
    }

    [Fact]
    public void HasCacheBreakpoint_FalseWhenEverythingIsVolatile()
    {
        var prompt = new SystemPrompt(new[] { Volatile(new string('v', 9000)) });

        Assert.False(prompt.HasCacheBreakpoint);
        Assert.Equal(0, prompt.StableCharCount);
    }

    [Fact]
    public void ImplicitFromString_IsWhollyStable()
    {
        SystemPrompt prompt = new string('s', 5000);

        Assert.True(prompt.HasCacheBreakpoint);
        Assert.All(prompt.Segments, s => Assert.Equal(SystemPromptStability.Stable, s.Stability));
    }

    [Fact]
    public void ImplicitFromNullOrBlank_IsEmpty()
    {
        SystemPrompt fromNull = (string?)null;
        SystemPrompt fromBlank = "   ";

        Assert.True(fromNull.IsEmpty);
        Assert.True(fromBlank.IsEmpty);
        Assert.False(fromNull.HasCacheBreakpoint);
    }

    [Fact]
    public void PromptIsStableAcrossTurns_WhenOnlyTheVolatileTailChanges()
    {
        // The property the whole change exists to guarantee: two consecutive turns differing only
        // in canvas state must present a byte-identical cacheable prefix.
        string preamble = new string('p', 6000);
        var turn1 = new SystemPrompt(new[] { Stable(preamble), Volatile("canvas rev 1") });
        var turn2 = new SystemPrompt(new[] { Stable(preamble), Volatile("canvas rev 2 — quite different") });

        Assert.Equal(turn1.StablePrefix, turn2.StablePrefix);
        Assert.NotEqual(turn1.Text, turn2.Text);
    }
}
