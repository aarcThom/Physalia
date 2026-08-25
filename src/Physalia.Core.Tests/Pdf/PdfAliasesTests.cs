// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.Pdf;
using Xunit;

namespace Physalia.Core.Tests.Pdf;

public class PdfAliasesTests
{
    [Theory]
    [InlineData("A-101 Floor Plan", "a-101-floor-plan")]
    [InlineData("24031 - ACME Tower (Rev C)", "24031-acme-tower-rev-c")]
    [InlineData("  spaced  out  ", "spaced-out")]
    [InlineData("under_scores.and.dots", "under-scores-and-dots")]
    [InlineData("Already-Fine", "already-fine")]
    public void Sanitize_CollapsesToOneLowercaseToken(string input, string expected) =>
        Assert.Equal(expected, PdfAliases.Sanitize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("...")]
    public void Sanitize_NothingUsable_FallsBack(string? input) =>
        Assert.Equal(PdfAliases.Fallback, PdfAliases.Sanitize(input));

    [Fact]
    public void Sanitize_NeverLeavesALeadingOrTrailingSeparator()
    {
        Assert.Equal("plan", PdfAliases.Sanitize("--plan--"));
        Assert.Equal("plan", PdfAliases.Sanitize("  (plan)  "));
    }

    [Fact]
    public void Sanitize_LongNameIsTruncatedWithoutATrailingSeparator()
    {
        string alias = PdfAliases.Sanitize(new string('a', 40) + " " + new string('b', 40));
        Assert.True(alias.Length <= 48);
        Assert.DoesNotContain("--", alias, StringComparison.Ordinal);
        Assert.False(alias.EndsWith('-'));
    }

    [Fact]
    public void FromFileName_UsesTheStemWithoutTheExtension() =>
        Assert.Equal("a-101-floor-plan", PdfAliases.FromFileName(@"C:\jobs\A-101 Floor Plan.pdf"));

    [Fact]
    public void FromFileName_DoesNotLeakThePath() =>
        Assert.Equal("plan", PdfAliases.FromFileName(@"C:\some\deep\folder\plan.pdf"));

    [Fact]
    public void Unique_UnusedAliasIsReturnedAsIs() =>
        Assert.Equal("plan", PdfAliases.Unique("plan", new[] { "other" }));

    [Fact]
    public void Unique_CollisionsGetANumericSuffix()
    {
        Assert.Equal("plan-2", PdfAliases.Unique("plan", new[] { "plan" }));
        Assert.Equal("plan-3", PdfAliases.Unique("plan", new[] { "plan", "plan-2" }));
    }

    [Fact]
    public void Unique_CollisionCheckIgnoresCase() =>
        Assert.Equal("plan-2", PdfAliases.Unique("Plan", new[] { "PLAN" }));

    [Fact]
    public void Unique_SanitizesBeforeComparing() =>
        Assert.Equal("floor-plan-2", PdfAliases.Unique("Floor Plan", new[] { "floor-plan" }));
}
