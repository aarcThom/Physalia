// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Physalia.Core.Config;
using Xunit;

namespace Physalia.Core.Tests.Config;

public class DataMigrationTests
{
    private const string Data = @"C:\users\me\AppData\Local\Physalia";
    private const string Install = @"C:\packages\8.0\Physalia\1.1.0";
    private const string Legacy = @"C:\packages\8.0\Physalia\1.1.0\Files";

    [Fact]
    public void MovesEachEntryOfAUserOwnedFolderIntoTheDataFolder()
    {
        var fs = new FakeProbe();
        fs.AddDirectory(Path.Combine(Legacy, "PROJECT_FILES", "curious-cake-soap-fun"));
        fs.AddDirectory(Path.Combine(Legacy, "MEMORIES", "GLOBAL"));

        IReadOnlyList<DataMigrationStep> plan = DataMigration.Plan(new[] { Legacy }, Data, fs);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, s => Assert.False(s.Blocked));
        Assert.Contains(plan, s => s.Destination == Path.Combine(Data, "PROJECT_FILES", "curious-cake-soap-fun"));
        Assert.Contains(plan, s => s.Destination == Path.Combine(Data, "MEMORIES", "GLOBAL"));
    }

    [Fact]
    public void ShippedContentIsNotMigrated()
    {
        // SYSTEM_PROMPTS, CLUSTERS and PRESETS/Physalia stay in the package and are read from there.
        // Moving them would strand the user on the version that happened to be installed the day the
        // migration ran, and every later fix to a shipped preamble would never reach them.
        var fs = new FakeProbe();
        fs.AddFile(Path.Combine(Legacy, "SYSTEM_PROMPTS", "PREAMBLE", "Node Graph.txt"));
        fs.AddFile(Path.Combine(Legacy, "CLUSTERS", "fancy_handrail.ghcluster"));
        fs.AddFile(Path.Combine(Legacy, "PRESETS", "Physalia", "01 - Talk to a Model.phy"));

        Assert.Empty(DataMigration.Plan(new[] { Legacy }, Data, fs));
    }

    [Fact]
    public void UserAndCommunityPresetsAreMigratedButNotTheShippedOnesBesideThem()
    {
        var fs = new FakeProbe();
        fs.AddFile(Path.Combine(Legacy, "PRESETS", "Physalia", "shipped.phy"));
        fs.AddFile(Path.Combine(Legacy, "PRESETS", "User", "mine.phy"));
        fs.AddFile(Path.Combine(Legacy, "PRESETS", "Community", "theirs.phy"));

        IReadOnlyList<DataMigrationStep> plan = DataMigration.Plan(new[] { Legacy }, Data, fs);

        Assert.Equal(
            new[] { "mine.phy", "theirs.phy" },
            plan.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void AnEntryAlreadyInTheDataFolderIsLeftAloneRatherThanMergedOver()
    {
        var fs = new FakeProbe();
        fs.AddDirectory(Path.Combine(Legacy, "PROJECT_FILES", "tower"));
        fs.AddDirectory(Path.Combine(Data, "PROJECT_FILES", "tower"));

        DataMigrationStep step = Assert.Single(DataMigration.Plan(new[] { Legacy }, Data, fs));

        Assert.True(step.Blocked);
        Assert.Empty(fs.Moves);
    }

    [Fact]
    public void PackagingArtifactsAreNotUserData()
    {
        var fs = new FakeProbe();
        fs.AddFile(Path.Combine(Legacy, "PROJECT_FILES", ".gitkeep"));
        fs.AddFile(Path.Combine(Legacy, "PROJECT_FILES", "README.md"));
        fs.AddFile(Path.Combine(Legacy, "MEMORIES", "README.md"));

        Assert.Empty(DataMigration.Plan(new[] { Legacy }, Data, fs));
    }

    [Fact]
    public void TheSameNameInTwoLegacyRootsMigratesOnceFromThePreferredOne()
    {
        // The second copy is blocked by a destination the FIRST step has not created yet, which is
        // why the plan tracks what it has claimed rather than only asking the filesystem.
        const string older = @"C:\packages\8.0\Physalia\1.0.0\Files";

        var fs = new FakeProbe();
        fs.AddDirectory(Path.Combine(Legacy, "MEMORIES", "LOCAL"));
        fs.AddDirectory(Path.Combine(older, "MEMORIES", "LOCAL"));

        IReadOnlyList<DataMigrationStep> plan = DataMigration.Plan(new[] { Legacy, older }, Data, fs);

        Assert.Equal(2, plan.Count);
        Assert.Equal(Path.Combine(Legacy, "MEMORIES", "LOCAL"), plan[0].Source);
        Assert.False(plan[0].Blocked);
        Assert.True(plan[1].Blocked);
    }

    [Fact]
    public void RunMovesEveryUnblockedStepAndReportsTheRest()
    {
        var fs = new FakeProbe();
        fs.AddDirectory(Path.Combine(Legacy, "PROJECT_FILES", "tower"));
        fs.AddDirectory(Path.Combine(Legacy, "PROJECT_FILES", "shed"));
        fs.AddDirectory(Path.Combine(Data, "PROJECT_FILES", "shed"));

        DataMigrationReport report = DataMigration.Run(DataMigration.Plan(new[] { Legacy }, Data, fs), fs);

        Assert.Equal(1, report.Moved);
        Assert.Single(report.Blocked);
        Assert.Empty(report.Failed);
        Assert.True(report.Any);
        Assert.Equal(
            new[] { (Path.Combine(Legacy, "PROJECT_FILES", "tower"), Path.Combine(Data, "PROJECT_FILES", "tower")) },
            fs.Moves);
    }

    [Fact]
    public void OneFailedMoveDoesNotStopTheOthers()
    {
        var fs = new FakeProbe { FailOn = "tower" };
        fs.AddDirectory(Path.Combine(Legacy, "PROJECT_FILES", "tower"));
        fs.AddDirectory(Path.Combine(Legacy, "PROJECT_FILES", "shed"));

        DataMigrationReport report = DataMigration.Run(DataMigration.Plan(new[] { Legacy }, Data, fs), fs);

        Assert.Equal(1, report.Moved);
        Assert.Single(report.Failed);
        Assert.Contains("tower", report.Failed[0], StringComparison.Ordinal);
    }

    [Fact]
    public void NothingToDoIsNotWorthReporting()
    {
        var fs = new FakeProbe();

        DataMigrationReport report = DataMigration.Run(DataMigration.Plan(new[] { Legacy }, Data, fs), fs);

        Assert.False(report.Any);
    }

    [Fact]
    public void BlockedAloneIsNotWorthReporting()
    {
        // A package that ships a demo project folder blocks it on every single update. Saying so
        // each time is how a user learns to ignore the line that matters.
        var fs = new FakeProbe();
        fs.AddDirectory(Path.Combine(Legacy, "PROJECT_FILES", "comfy-render"));
        fs.AddDirectory(Path.Combine(Data, "PROJECT_FILES", "comfy-render"));

        DataMigrationReport report = DataMigration.Run(DataMigration.Plan(new[] { Legacy }, Data, fs), fs);

        Assert.False(report.Any);
        Assert.Single(report.Blocked);
    }

    [Fact]
    public void RootsStartWithThisInstallThenTheOtherVersionsNewestFirst()
    {
        var fs = new FakeProbe();
        fs.AddDirectory(@"C:\packages\8.0\Physalia\1.1.0");
        fs.AddDirectory(@"C:\packages\8.0\Physalia\1.0.9");
        fs.AddDirectory(@"C:\packages\8.0\Physalia\1.0.10");
        fs.AddDirectory(@"C:\packages\8.0\Physalia\staging");

        IReadOnlyList<string> roots = DataMigration.OrderRoots(Install, fs);

        Assert.Equal(
            new[]
            {
                Path.Combine(Install, "Files"),
                @"C:\packages\8.0\Physalia\1.0.10\Files",
                @"C:\packages\8.0\Physalia\1.0.9\Files",
                @"C:\packages\8.0\Physalia\staging\Files",
            },
            roots);
    }

    [Fact]
    public void RootsAreJustThisInstallWhenItHasNoSiblings()
    {
        var fs = new FakeProbe();

        Assert.Equal(new[] { Path.Combine(Install, "Files") }, DataMigration.OrderRoots(Install, fs));
    }

    // An in-memory filesystem: every path added is an entry, and a directory is anything a path
    // passes through. Enough for the planner, which only ever asks what is where.
    private sealed class FakeProbe : IMigrationProbe
    {
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Source, string Destination)> Moves { get; } = new();

        /// <summary>Gets or sets an entry name whose move fails, for the failure path.</summary>
        public string? FailOn { get; set; }

        public void AddFile(string path)
        {
            _files.Add(path);
            AddParents(path);
        }

        public void AddDirectory(string path)
        {
            _directories.Add(path);
            AddParents(path);
        }

        public bool DirectoryExists(string path) => _directories.Contains(path);

        public bool EntryExists(string path) => _files.Contains(path) || _directories.Contains(path);

        public IReadOnlyList<string> EntryNames(string path) => Children(_files.Concat(_directories), path);

        public IReadOnlyList<string> DirectoryNames(string path) => Children(_directories, path);

        public string? Move(string source, string destination)
        {
            if (FailOn is not null && Path.GetFileName(source).Equals(FailOn, StringComparison.OrdinalIgnoreCase))
            {
                return "held open by another process";
            }

            Moves.Add((source, destination));
            return null;
        }

        private static IReadOnlyList<string> Children(IEnumerable<string> paths, string parent)
        {
            string prefix = parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            return paths
                .Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Substring(prefix.Length).Split(Path.DirectorySeparatorChar)[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void AddParents(string path)
        {
            string? parent = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(parent))
            {
                if (!_directories.Add(parent))
                {
                    return;
                }

                parent = Path.GetDirectoryName(parent);
            }
        }
    }
}
