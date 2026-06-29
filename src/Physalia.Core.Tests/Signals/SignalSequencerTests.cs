// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Signals;
using Xunit;

namespace Physalia.Core.Tests.Signals;

public class SignalSequencerTests
{
    [Fact]
    public void Next_IsStrictlyIncreasing()
    {
        long a = SignalSequencer.Next();
        long b = SignalSequencer.Next();

        Assert.True(b > a, $"Expected {b} > {a}");
    }

    [Fact]
    public void Next_NeverReturnsZero()
    {
        // Zero is reserved to mean "never / unset".
        for (int i = 0; i < 100; i++)
        {
            Assert.NotEqual(0, SignalSequencer.Next());
        }
    }

    [Fact]
    public void Next_IsUniqueUnderConcurrency()
    {
        const int count = 2000;
        var results = new long[count];

        Parallel.For(0, count, i => results[i] = SignalSequencer.Next());

        Assert.Equal(count, results.Distinct().Count());
    }
}
