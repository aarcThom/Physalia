// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Triggers;
using Xunit;

namespace Physalia.Core.Tests.Triggers;

public class FolderChangeSummaryTests
{
    private static FolderChange Change(string path, FolderChangeKind kind) => new(path, kind);

    [Fact]
    public void OneFileWrittenSeveralTimes_FoldsToOneEntry()
    {
        // The normal case for a single saved file: the OS reports the write, the timestamp and the
        // size separately, and reporting three changes would tell the model the file changed thrice.
        IReadOnlyList<FolderChange> result = FolderChangeSummary.Coalesce(new[]
        {
            Change("index.csv", FolderChangeKind.Changed),
            Change("index.csv", FolderChangeKind.Changed),
            Change("index.csv", FolderChangeKind.Changed),
        });

        FolderChange only = Assert.Single(result);
        Assert.Equal("index.csv", only.RelativePath);
        Assert.Equal(FolderChangeKind.Changed, only.Kind);
    }

    [Fact]
    public void AddedThenChanged_StaysAdded()
    {
        // The write is the copy finishing. The interesting fact is that the file is new.
        IReadOnlyList<FolderChange> result = FolderChangeSummary.Coalesce(new[]
        {
            Change("tile.las", FolderChangeKind.Added),
            Change("tile.las", FolderChangeKind.Changed),
        });

        Assert.Equal(FolderChangeKind.Added, Assert.Single(result).Kind);
    }

    [Fact]
    public void RenamedThenChanged_StaysRenamed()
    {
        IReadOnlyList<FolderChange> result = FolderChangeSummary.Coalesce(new[]
        {
            Change("final.csv", FolderChangeKind.Renamed),
            Change("final.csv", FolderChangeKind.Changed),
        });

        Assert.Equal(FolderChangeKind.Renamed, Assert.Single(result).Kind);
    }

    [Fact]
    public void AddedThenRemoved_IsNotReportedAtAll()
    {
        // A temporary file. Nothing downstream can act on it, so waking the pipeline is worse
        // than staying quiet.
        IReadOnlyList<FolderChange> result = FolderChangeSummary.Coalesce(new[]
        {
            Change("~tmp.las", FolderChangeKind.Added),
            Change("~tmp.las", FolderChangeKind.Removed),
        });

        Assert.Empty(result);
    }

    [Fact]
    public void ChangedThenRemoved_IsReportedAsRemoved()
    {
        IReadOnlyList<FolderChange> result = FolderChangeSummary.Coalesce(new[]
        {
            Change("index.csv", FolderChangeKind.Changed),
            Change("index.csv", FolderChangeKind.Removed),
        });

        Assert.Equal(FolderChangeKind.Removed, Assert.Single(result).Kind);
    }

    [Fact]
    public void PathsAreComparedIgnoringSeparatorStyleAndCase()
    {
        IReadOnlyList<FolderChange> result = FolderChangeSummary.Coalesce(new[]
        {
            Change("sub\\Tile.las", FolderChangeKind.Added),
            Change("sub/tile.las", FolderChangeKind.Changed),
        });

        FolderChange only = Assert.Single(result);
        Assert.Equal("sub/Tile.las", only.RelativePath);
        Assert.Equal(FolderChangeKind.Added, only.Kind);
    }

    [Fact]
    public void OrderIsWhenEachFileWasFirstSeen()
    {
        IReadOnlyList<FolderChange> result = FolderChangeSummary.Coalesce(new[]
        {
            Change("b.las", FolderChangeKind.Added),
            Change("a.las", FolderChangeKind.Added),
            Change("b.las", FolderChangeKind.Changed),
        });

        Assert.Equal(new[] { "b.las", "a.las" }, result.Select(c => c.RelativePath));
    }

    [Fact]
    public void NullsAndBlankPathsAreIgnored()
    {
        IReadOnlyList<FolderChange> result = FolderChangeSummary.Coalesce(new[]
        {
            Change("  ", FolderChangeKind.Added),
            Change("real.las", FolderChangeKind.Added),
        });

        Assert.Equal("real.las", Assert.Single(result).RelativePath);
    }

    [Fact]
    public void NoChanges_DescribesAsEmpty_SoNoBlankSignalIsMinted()
    {
        Assert.Equal(string.Empty, FolderChangeSummary.Describe("C:/p", Array.Empty<FolderChange>()));
        Assert.Equal(string.Empty, FolderChangeSummary.Describe("C:/p", null));
    }

    [Fact]
    public void DescribeNamesTheFolderOnceAndCountsByKind()
    {
        string text = FolderChangeSummary.Describe(
            "C:/Files/PROJECT_FILES/curious-cake-soap-fun",
            new[]
            {
                Change("tiles/a.las", FolderChangeKind.Added),
                Change("tiles/b.las", FolderChangeKind.Added),
                Change("index.csv", FolderChangeKind.Changed),
            });

        Assert.Contains("C:/Files/PROJECT_FILES/curious-cake-soap-fun", text);
        Assert.Contains("2 added", text);
        Assert.Contains("1 changed", text);
        Assert.Contains("+ tiles/a.las", text);
        Assert.Contains("~ index.csv", text);
    }

    [Fact]
    public void DescribeCountsTheRestOnceItHasNamedEnough()
    {
        FolderChange[] many = Enumerable.Range(0, 30)
            .Select(i => Change($"f{i}.las", FolderChangeKind.Added))
            .ToArray();

        string text = FolderChangeSummary.Describe("C:/p", many, maxListed: 5);

        Assert.Contains("f4.las", text);
        Assert.DoesNotContain("f5.las", text);
        Assert.Contains("and 25 more", text);
    }
}
