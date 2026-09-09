// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Text.RegularExpressions;
using Physalia.Core.Config;
using Xunit;

namespace Physalia.Core.Tests.Config;

public class ReleaseNotesTests
{
    private const string Changelog = """
# Changelog

Front matter, which belongs to no release.

## 1.2.0 — 2026-10-01

- Something new.
- Something fixed.

### A subheading inside the release

Still part of 1.2.0.

## 1.1.0

- The previous release.

## 1.0.0

- The first one.
""";

    [Fact]
    public void FindsTheSectionForAVersion()
    {
        string? notes = ReleaseNotes.SectionFor(Changelog, "1.2.0");

        Assert.NotNull(notes);
        Assert.Contains("Something new.", notes, StringComparison.Ordinal);
        Assert.Contains("Still part of 1.2.0.", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("The previous release.", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void ASubheadingDoesNotEndTheSection()
    {
        // "###" is something inside a release; only "##" opens or closes one.
        string? notes = ReleaseNotes.SectionFor(Changelog, "1.2.0");

        Assert.Contains("A subheading inside the release", notes!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.0")]
    [InlineData("1.2.0.0")]
    [InlineData("v1.2.0")]
    public void TheVersionIsMatchedAsAVersionNotAsAString(string asked)
    {
        // The stamp records four parts and a changelog heading rarely writes them, so a string
        // comparison here would find nothing on almost every release.
        Assert.NotNull(ReleaseNotes.SectionFor(Changelog, asked));
    }

    [Fact]
    public void TheLastSectionRunsToTheEndOfTheFile()
    {
        Assert.Equal("- The first one.", ReleaseNotes.SectionFor(Changelog, "1.0.0"));
    }

    [Fact]
    public void FrontMatterBelongsToNoRelease()
    {
        Assert.Null(ReleaseNotes.SectionFor(Changelog, "0.9.0"));
    }

    [Fact]
    public void AnEmptySectionIsNoSection()
    {
        Assert.Null(ReleaseNotes.SectionFor("## 1.0.0\n\n\n## 0.9.0\n\n- old", "1.0.0"));
    }

    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("", "1.0.0")]
    [InlineData("## 1.0.0\n- notes", null)]
    [InlineData("## 1.0.0\n- notes", "not-a-version")]
    public void NonsenseYieldsNothingRatherThanThrowing(string? markdown, string? version)
    {
        Assert.Null(ReleaseNotes.SectionFor(markdown, version));
    }

    [Fact]
    public void TheShippedChangelogHasASectionForTheShippedVersion()
    {
        // The one thing here that can silently stop being true: a release bumps the csproj version
        // and forgets the changelog, and the update notice quietly loses its detail. Both values are
        // read from the repo rather than restated, so this fails on the bump and not a release later.
        string root = RepoRoot();
        string csproj = File.ReadAllText(Path.Combine(root, "src", "Physalia.GH", "Physalia.GH.csproj"));
        Match version = Regex.Match(csproj, @"<Version>([^<]+)</Version>");

        Assert.True(version.Success, "Physalia.GH.csproj no longer declares a <Version>.");

        Assert.NotNull(ReleaseNotes.SectionFromFile(
            Path.Combine(root, "Files", "CHANGELOG.md"),
            version.Groups[1].Value.Trim()));
    }

    // Walks up from the test bin directory to the repo root — the folder holding both Files and src.
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Files", "CHANGELOG.md")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("Files/CHANGELOG.md was not found above the test directory.");
    }
}
