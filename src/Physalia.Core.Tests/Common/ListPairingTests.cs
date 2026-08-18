// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Common;
using Xunit;

namespace Physalia.Core.Tests.Common;

public class ListPairingTests
{
    [Fact]
    public void EqualLengths_PairOneToOne()
    {
        var notes = new[] { "a", "b", "c" };

        Assert.Equal("a", ListPairing.MatchLongest(notes, 0));
        Assert.Equal("b", ListPairing.MatchLongest(notes, 1));
        Assert.Equal("c", ListPairing.MatchLongest(notes, 2));
    }

    [Fact]
    public void ShorterList_ReusesItsLastEntry()
    {
        // Longest-list matching: three notes against ten positions means positions 2..9 all take the
        // third note, rather than the pairing running out.
        var notes = new[] { "a", "b", "c" };

        Assert.Equal("c", ListPairing.MatchLongest(notes, 3));
        Assert.Equal("c", ListPairing.MatchLongest(notes, 9));
    }

    [Fact]
    public void SingleEntry_AppliesToEveryIndex()
    {
        var notes = new[] { "only" };

        Assert.Equal("only", ListPairing.MatchLongest(notes, 0));
        Assert.Equal("only", ListPairing.MatchLongest(notes, 50));
    }

    [Fact]
    public void EmptyList_YieldsDefault()
    {
        Assert.Null(ListPairing.MatchLongest(Array.Empty<string>(), 0));
    }

    [Fact]
    public void NegativeIndex_YieldsDefault()
    {
        Assert.Null(ListPairing.MatchLongest(new[] { "a" }, -1));
    }

    [Fact]
    public void ExtraEntries_AreIgnored_NotAnError()
    {
        // More notes than positions: the surplus is simply never asked for.
        var notes = new[] { "a", "b", "c", "d" };

        Assert.Equal("b", ListPairing.MatchLongest(notes, 1));
    }
}
