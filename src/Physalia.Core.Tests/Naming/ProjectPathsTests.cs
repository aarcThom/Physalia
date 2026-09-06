// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using Physalia.Core.Naming;
using Xunit;

namespace Physalia.Core.Tests.Naming;

public class ProjectPathsTests
{
    private const string Root = @"C:\plugin\Files\PROJECT_FILES";
    private const string DocFolder = @"D:\jobs\tower";

    [Fact]
    public void Blank_UsesTheHarnessName()
    {
        ProjectPathResolution result = ProjectPaths.Resolve(null, "curious-cake-soap-fun", Root, DocFolder);

        Assert.Equal(ProjectPathKind.Default, result.Kind);
        Assert.Equal(Path.Combine(Root, "curious-cake-soap-fun"), result.FullPath);
    }

    [Fact]
    public void GeneratedKey_SurvivesSanitizingUntouched()
    {
        // The reason the word list is lower-case letters only: what is on the canvas is what is on
        // disk, with no translation step for anyone to be surprised by.
        string name = FourWordKey.From(Guid.NewGuid());

        Assert.Equal(name, ProjectPaths.FolderKey(name));
    }

    [Fact]
    public void PlainName_IsAFolderUnderTheRoot()
    {
        ProjectPathResolution result = ProjectPaths.Resolve("site-survey", "unused", Root, DocFolder);

        Assert.Equal(ProjectPathKind.Named, result.Kind);
        Assert.Equal(Path.Combine(Root, "site-survey"), result.FullPath);
    }

    [Theory]
    [InlineData("./data")]
    [InlineData("../shared/las")]
    [InlineData("data/las")]
    [InlineData(@"data\las")]
    public void SeparatorMeansRelativeToTheSavedDocument(string typed)
    {
        ProjectPathResolution result = ProjectPaths.Resolve(typed, "unused", Root, DocFolder);

        Assert.Equal(ProjectPathKind.DocumentRelative, result.Kind);
        Assert.Equal(Path.GetFullPath(Path.Combine(DocFolder, typed)), result.FullPath);
    }

    [Fact]
    public void RelativePath_OnAnUnsavedDocument_SaysSoRatherThanFallingBack()
    {
        // Quietly redirecting to some other folder is how a user loses track of where files went.
        ProjectPathResolution result = ProjectPaths.Resolve("./data", "unused", Root, null);

        Assert.Equal(ProjectPathKind.Unresolvable, result.Kind);
        Assert.False(result.IsResolved);
        Assert.Contains("has not been saved", result.ProblemText);
    }

    [Theory]
    [InlineData(@"D:\Projects\lidar")]
    [InlineData(@"\\share\projects\lidar")]
    public void RootedPath_IsUsedVerbatim(string typed)
    {
        ProjectPathResolution result = ProjectPaths.Resolve(typed, "unused", Root, DocFolder);

        Assert.Equal(ProjectPathKind.Rooted, result.Kind);
        Assert.Equal(typed, result.FullPath);
    }

    [Fact]
    public void ANameCannotClimbOutOfTheRoot()
    {
        // A name is sanitized, so separators become dashes and it stays one folder deep.
        ProjectPathResolution result = ProjectPaths.Resolve("..", "unused", Root, DocFolder);

        Assert.Equal(ProjectPathKind.Named, result.Kind);
        Assert.Equal(Path.Combine(Root, "unnamed"), result.FullPath);
    }

    [Theory]
    [InlineData("my project", "my-project")]
    [InlineData("a/b", "a-b")]
    [InlineData("..", "unnamed")]
    [InlineData(".hidden", "hidden")]
    [InlineData("v1.2", "v1.2")]
    [InlineData("", "unnamed")]
    [InlineData(null, "unnamed")]
    public void FolderKey_ReducesToOneSafeName(string? typed, string expected)
    {
        Assert.Equal(expected, ProjectPaths.FolderKey(typed));
    }

    [Fact]
    public void MissingRoot_IsAProblemNotACrash()
    {
        Assert.Equal(ProjectPathKind.Unresolvable, ProjectPaths.Resolve(null, "n", string.Empty, DocFolder).Kind);
    }

    [Theory]
    [InlineData(@"C:\root", @"C:\root\a.txt", true)]
    [InlineData(@"C:\root", @"C:\root\sub\a.txt", true)]
    [InlineData(@"C:\root", @"C:\root", true)]
    [InlineData(@"C:\root", @"C:\root\..\a.txt", false)]
    [InlineData(@"C:\root", @"C:\rootless\a.txt", false)]
    [InlineData(@"C:\root", @"C:\other\a.txt", false)]
    public void IsContained_ComparesResolvedPaths(string root, string candidate, bool expected)
    {
        Assert.Equal(expected, ProjectPaths.IsContained(root, candidate));
    }

    [Fact]
    public void IsContained_IsNotFooledByASharedNamePrefix()
    {
        // The trailing-separator detail: "C:\rootless" starts with "C:\root" as a string but is not
        // inside it.
        Assert.False(ProjectPaths.IsContained(@"C:\root", @"C:\rootless"));
    }
}
