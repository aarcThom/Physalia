// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Physalia.Core.Config;
using Xunit;

namespace Physalia.Core.Tests.Config;

/// <summary>
/// The migration against a REAL filesystem. The planner's rules are covered without one in
/// <see cref="DataMigrationTests"/>; this covers the part that actually moves somebody's work, and
/// which a fake probe can only pretend to do.
/// </summary>
public class FileSystemMigrationProbeTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "physalia-migrate-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void CarriesAProjectFolderAndItsContentsAcross()
    {
        string legacy = Path.Combine(_root, "install", "Files");
        string data = Path.Combine(_root, "data");

        Write(Path.Combine(legacy, "PROJECT_FILES", "curious-cake-soap-fun", "downloads.json"), "{}");
        Write(Path.Combine(legacy, "PROJECT_FILES", "curious-cake-soap-fun", "PDF", "site.pdf"), "%PDF-1.4");
        Write(Path.Combine(legacy, "MEMORIES", "GLOBAL", "note.md"), "remember this");
        Write(Path.Combine(legacy, "PRESETS", "User", "mine.phy"), "PK");
        Write(Path.Combine(legacy, "PRESETS", "Physalia", "shipped.phy"), "PK");

        var probe = new FileSystemMigrationProbe();
        DataMigrationReport report = DataMigration.Run(
            DataMigration.Plan(new[] { legacy }, data, probe),
            probe);

        Assert.Equal(3, report.Moved);
        Assert.Empty(report.Failed);

        // Arrived, contents and nesting intact.
        Assert.Equal("{}", File.ReadAllText(Path.Combine(data, "PROJECT_FILES", "curious-cake-soap-fun", "downloads.json")));
        Assert.Equal("%PDF-1.4", File.ReadAllText(Path.Combine(data, "PROJECT_FILES", "curious-cake-soap-fun", "PDF", "site.pdf")));
        Assert.Equal("remember this", File.ReadAllText(Path.Combine(data, "MEMORIES", "GLOBAL", "note.md")));
        Assert.Equal("PK", File.ReadAllText(Path.Combine(data, "PRESETS", "User", "mine.phy")));

        // Gone from the install directory, so a later build does not find it and move it twice.
        Assert.False(Directory.Exists(Path.Combine(legacy, "PROJECT_FILES", "curious-cake-soap-fun")));

        // And the shipped preset is untouched: it is ours, not the user's.
        Assert.True(File.Exists(Path.Combine(legacy, "PRESETS", "Physalia", "shipped.phy")));
    }

    [Fact]
    public void RunningTwiceMovesNothingTheSecondTime()
    {
        string legacy = Path.Combine(_root, "install", "Files");
        string data = Path.Combine(_root, "data");
        Write(Path.Combine(legacy, "MEMORIES", "LOCAL", "harness", "note.md"), "x");

        var probe = new FileSystemMigrationProbe();
        Assert.Equal(1, Run(legacy, data, probe).Moved);

        DataMigrationReport second = Run(legacy, data, probe);
        Assert.False(second.Any);
        Assert.Equal(0, second.Moved);
    }

    [Fact]
    public void WhatIsAlreadyInTheDataFolderWins()
    {
        string legacy = Path.Combine(_root, "install", "Files");
        string data = Path.Combine(_root, "data");

        Write(Path.Combine(legacy, "PROJECT_FILES", "tower", "downloads.json"), "the old one");
        Write(Path.Combine(data, "PROJECT_FILES", "tower", "downloads.json"), "the one in use");

        DataMigrationReport report = Run(legacy, data, new FileSystemMigrationProbe());

        Assert.Equal(0, report.Moved);
        Assert.Single(report.Blocked);

        // Neither destroyed nor merged — both still there, for a human to reconcile.
        Assert.Equal("the one in use", File.ReadAllText(Path.Combine(data, "PROJECT_FILES", "tower", "downloads.json")));
        Assert.Equal("the old one", File.ReadAllText(Path.Combine(legacy, "PROJECT_FILES", "tower", "downloads.json")));
    }

    [Fact]
    public void FindsWhatAPreviousPACKAGEVERSIONLeftBehind()
    {
        // The case that rescues somebody who updated BEFORE this shipped: their data is in a sibling
        // version directory, which the install they are now running has never looked in.
        string packages = Path.Combine(_root, "packages", "8.0", "Physalia");
        string current = Path.Combine(packages, "1.1.0");
        string data = Path.Combine(_root, "data");

        Directory.CreateDirectory(Path.Combine(current, "Files"));
        Write(Path.Combine(packages, "1.0.0", "Files", "MEMORIES", "GLOBAL", "note.md"), "from the old version");

        var probe = new FileSystemMigrationProbe();
        IReadOnlyList<string> roots = DataMigration.OrderRoots(current, probe);
        DataMigrationReport report = DataMigration.Run(DataMigration.Plan(roots, data, probe), probe);

        Assert.Equal(1, report.Moved);
        Assert.Equal("from the old version", File.ReadAllText(Path.Combine(data, "MEMORIES", "GLOBAL", "note.md")));
    }

    [Fact]
    public void AnInstallWithNothingInItIsSilent()
    {
        string legacy = Path.Combine(_root, "install", "Files");
        Directory.CreateDirectory(legacy);

        Assert.False(Run(legacy, Path.Combine(_root, "data"), new FileSystemMigrationProbe()).Any);
    }

    private static DataMigrationReport Run(string legacy, string data, IMigrationProbe probe) =>
        DataMigration.Run(DataMigration.Plan(new[] { legacy }, data, probe), probe);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
