// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.Config;
using Xunit;

namespace Physalia.Core.Tests.Config;

public class PhyVersionTests
{
    [Theory]
    [InlineData(1, 0, 0, 0, "1.0")]
    [InlineData(1, 2, 0, 0, "1.2")]
    [InlineData(1, 2, 3, 0, "1.2.3")]
    [InlineData(1, 2, 3, 4, "1.2.3.4")]
    public void DisplayDropsTrailingZeroesButNeverGoesBelowTwoParts(
        int major, int minor, int build, int revision, string expected)
    {
        Assert.Equal(expected, PhyVersion.Display(new Version(major, minor, build, revision)));
    }

    [Fact]
    public void AZeroPartWithSomethingAfterItIsKept()
    {
        // 1.0.3 is not 1.3, and a version line that said so would send somebody to the wrong build.
        Assert.Equal("1.0.3", PhyVersion.Display(new Version(1, 0, 3, 0)));
    }

    [Fact]
    public void TheFullFormIsNeverTrimmed()
    {
        // What gets recorded and compared: two builds differing only in a trailing part are still
        // two builds, and the update notice has to be able to tell them apart.
        PhyVersionInfo info = PhyVersion.For(new Version(1, 2, 0, 0));

        Assert.Equal("1.2", info.Display);
        Assert.Equal("1.2.0.0", info.Full);
    }

    [Fact]
    public void AnAssemblyWithNoVersionSaysSoRatherThanShowingNothing()
    {
        PhyVersionInfo info = PhyVersion.For(null);

        Assert.Equal("unknown", info.Display);
        Assert.Equal("0.0.0.0", info.Full);
    }
}
