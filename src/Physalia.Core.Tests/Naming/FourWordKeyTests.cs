// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Physalia.Core.Naming;
using Xunit;

namespace Physalia.Core.Tests.Naming;

public class FourWordKeyTests
{
    [Fact]
    public void WordList_IsExactlyTwoHundredAndFiftySixUniqueWords()
    {
        Assert.Equal(256, FourWordKey.Words.Count);
        Assert.Equal(256, FourWordKey.Words.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryWord_IsLowerCaseLettersOnly()
    {
        // The name becomes a folder name verbatim, so anything a file name cannot hold — a space, a
        // separator, an accent — would make the canvas and the disk disagree.
        Assert.All(FourWordKey.Words, word => Assert.Matches("^[a-z]{3,7}$", word));
    }

    [Fact]
    public void From_IsDeterministic()
    {
        var id = Guid.NewGuid();
        Assert.Equal(FourWordKey.From(id), FourWordKey.From(id));
    }

    [Fact]
    public void From_ProducesFourHyphenSeparatedWords()
    {
        string name = FourWordKey.From(Guid.NewGuid());
        string[] parts = name.Split('-');

        Assert.Equal(4, parts.Length);
        Assert.All(parts, part => Assert.Contains(part, FourWordKey.Words));
    }

    [Fact]
    public void From_UsesTheFirstFourBytes()
    {
        // Byte order is Guid's own; what matters is that four DIFFERENT bytes are consumed, so two
        // ids differing in only one of them get different names.
        var a = new Guid(new byte[] { 0, 1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        var b = new Guid(new byte[] { 0, 1, 2, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        Assert.NotEqual(FourWordKey.From(a), FourWordKey.From(b));
        Assert.Equal(
            string.Join("-", FourWordKey.Words[0], FourWordKey.Words[1], FourWordKey.Words[2], FourWordKey.Words[3]),
            FourWordKey.From(a));
    }

    [Fact]
    public void DifferentIds_OverwhelminglyGetDifferentNames()
    {
        // Not a uniqueness guarantee — 2^32 names collide by the birthday bound eventually — but a
        // thousand harnesses in one session must not be where it starts happening.
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 1000; i++)
        {
            names.Add(FourWordKey.From(Guid.NewGuid()));
        }

        Assert.True(names.Count > 995, $"only {names.Count} distinct names from 1000 ids");
    }

    [Fact]
    public void IsGeneratedShape_AcceptsWhatFromProduces()
    {
        Assert.True(FourWordKey.IsGeneratedShape(FourWordKey.From(Guid.NewGuid())));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Harness")]
    [InlineData("vancouver-lidar")]
    [InlineData("cake-jam-plum")]
    [InlineData("cake-jam-plum-pear-bean")]
    [InlineData("cake-jam-plum-notaword")]
    [InlineData("Cake-jam-plum-pear")]
    public void IsGeneratedShape_RejectsAnythingElse(string? name)
    {
        Assert.False(FourWordKey.IsGeneratedShape(name));
    }

    [Fact]
    public void IsGeneratedShape_IsWhatDistinguishesAPastedCopy()
    {
        // The case it exists for: a pasted harness carries the ORIGINAL's name and its own new id, so
        // comparing the name against its own derivation says "not generated" when it plainly is.
        var original = Guid.NewGuid();
        var pasted = Guid.NewGuid();
        string carried = FourWordKey.From(original);

        Assert.NotEqual(carried, FourWordKey.From(pasted));
        Assert.True(FourWordKey.IsGeneratedShape(carried));
    }
}
