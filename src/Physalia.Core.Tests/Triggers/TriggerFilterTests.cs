// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Triggers;
using Xunit;

namespace Physalia.Core.Tests.Triggers;

public class TriggerFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankFilter_MatchesEverything(string? filter)
    {
        Assert.True(TriggerFilter.Matches("anything.las", filter));
    }

    [Fact]
    public void SeparatorsOnly_IsTreatedAsNoFilter_NotAsMatchNothing()
    {
        Assert.True(TriggerFilter.Matches("anything.las", ";;,"));
    }

    [Theory]
    [InlineData("*.las", "tile.las", true)]
    [InlineData("*.las", "tile.LAS", true)]
    [InlineData("*.las", "tile.laz", false)]
    [InlineData("*.csv;*.txt", "index.txt", true)]
    [InlineData("*.csv;*.txt", "index.json", false)]
    [InlineData("site-*.json", "site-a.json", true)]
    [InlineData("site-*.json", "other-a.json", false)]
    [InlineData("tile-?.las", "tile-a.las", true)]
    [InlineData("tile-?.las", "tile-ab.las", false)]
    public void PatternsMatchTheFileName(string filter, string name, bool expected)
    {
        Assert.Equal(expected, TriggerFilter.Matches(name, filter));
    }

    [Fact]
    public void RegexMetacharactersAreLiteral_NotPattern()
    {
        // "report(final).*" must match the literal parenthesis, not be read as a regex group.
        Assert.True(TriggerFilter.Matches("report(final).csv", "report(final).*"));
        Assert.False(TriggerFilter.Matches("reportfinal.csv", "report(final).*"));
    }

    [Fact]
    public void PatternIsAnchored_SoASuffixMatchIsNotAContainsMatch()
    {
        Assert.False(TriggerFilter.Matches("my.las.bak", "*.las"));
    }

    [Fact]
    public void BlankFileName_WithARealFilter_NeverMatches()
    {
        Assert.False(TriggerFilter.Matches(null, "*.las"));
        Assert.False(TriggerFilter.Matches("  ", "*.las"));
    }
}
