// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Budget;
using Xunit;

namespace Physalia.Core.Tests.Budget;

public class SpendPolicyTests
{
    [Fact]
    public void NoLimits_AllowsEverything()
    {
        Assert.True(SpendPolicy.Check(new Spend(999_999_999, 10_000), SpendLimits.Unlimited).Allowed);
    }

    [Fact]
    public void NullsAreTreatedAsNothingSpentAndNothingLimited()
    {
        Assert.True(SpendPolicy.Check(null, null).Allowed);
    }

    [Fact]
    public void UnderBothLimits_IsAllowed()
    {
        Assert.True(SpendPolicy.Check(new Spend(100, 1), new SpendLimits(200, 5)).Allowed);
    }

    [Fact]
    public void ExactlyAtTheTokenLimit_IsRefused()
    {
        // At the limit means spent, not "one more for luck": the budget was 200 and 200 is gone.
        SpendVerdict verdict = SpendPolicy.Check(new Spend(200, 1), new SpendLimits(200, 0));

        Assert.False(verdict.Allowed);
        Assert.Contains("token budget", verdict.Reason);
    }

    [Fact]
    public void ExactlyAtTheCallLimit_IsRefused()
    {
        SpendVerdict verdict = SpendPolicy.Check(new Spend(1, 5), new SpendLimits(0, 5));

        Assert.False(verdict.Allowed);
        Assert.Contains("call budget", verdict.Reason);
    }

    [Fact]
    public void CallsAreCheckedBeforeTokens_SoTheReasonNamesTheCheaperFixFirst()
    {
        // Both blown. The call count is the axis a person can reason about without a calculator, so
        // that is the one the message leads with.
        SpendVerdict verdict = SpendPolicy.Check(new Spend(999, 99), new SpendLimits(10, 10));

        Assert.False(verdict.Allowed);
        Assert.Contains("call budget", verdict.Reason);
    }

    [Fact]
    public void ZeroOnOneAxis_LeavesThatAxisUnlimited()
    {
        // "Cap the tokens, I do not care how many calls it takes" needs no second switch to say it.
        Assert.True(SpendPolicy.Check(new Spend(1, 10_000), new SpendLimits(200, 0)).Allowed);
        Assert.True(SpendPolicy.Check(new Spend(10_000, 1), new SpendLimits(0, 200)).Allowed);
    }

    [Fact]
    public void ALimitLoweredBelowWhatIsAlreadySpent_RefusesRatherThanUnderflowing()
    {
        Assert.False(SpendPolicy.Check(new Spend(5_000, 3), new SpendLimits(100, 0)).Allowed);
    }

    [Fact]
    public void PlusCountsOneCallAndClampsNegativeTokens()
    {
        Spend after = Spend.Nothing.Plus(120).Plus(-5);

        Assert.Equal(120, after.Tokens);
        Assert.Equal(2, after.Calls);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1k")]
    [InlineData(182_431, "182.4k")]
    [InlineData(2_500_000, "2.5M")]
    public void TokensFormatCompactly(long tokens, string expected)
    {
        Assert.Equal(expected, SpendPolicy.Tokens(tokens));
    }

    [Fact]
    public void DescribeShowsBothAxesAgainstTheirLimits()
    {
        string text = SpendPolicy.Describe(new Spend(182_431, 14), new SpendLimits(200_000, 50));

        Assert.Equal("182.4k / 200k tokens · 14 / 50 calls", text);
    }

    [Fact]
    public void DescribeOmitsALimitThatIsNotSet()
    {
        string text = SpendPolicy.Describe(new Spend(1_200, 1), SpendLimits.Unlimited);

        Assert.Equal("1.2k tokens · 1 call", text);
    }
}
