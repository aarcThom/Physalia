// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Physalia.Core.Common;
using Physalia.Core.Files;
using Xunit;

namespace Physalia.Core.Tests.Files;

public sealed class FileReadTests : IDisposable
{
    private readonly string _root;

    public FileReadTests()
    {
        this._root = Path.Combine(Path.GetTempPath(), "read-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this._root, true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(this._root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string WriteBytes(string name, byte[] content)
    {
        string path = Path.Combine(this._root, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void List_ReportsRelativePathsNewestFirst()
    {
        this.Write("old.txt", "a");
        System.Threading.Thread.Sleep(20);
        this.Write("sub/new.json", "{}");

        Assert.True(FileRead.List(this._root).IsOk(out IReadOnlyList<ProjectFileInfo>? files, out _));
        Assert.Equal(2, files!.Count);
        Assert.Equal("sub/new.json", files[0].Path);
    }

    [Fact]
    public void List_OfAMissingFolderIsEmptyRatherThanAnError()
    {
        Assert.True(FileRead.List(Path.Combine(this._root, "nothing-here"))
            .IsOk(out IReadOnlyList<ProjectFileInfo>? files, out _));
        Assert.Empty(files!);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("../../escape.txt")]
    [InlineData("sub/../../escape.txt")]
    public void TryResolve_RefusesAPathOutsideTheProjectFolder(string path)
    {
        Assert.False(FileRead.TryResolve(this._root, path, out _, out string problem));
        Assert.Contains("outside the project folder", problem);
    }

    [Fact]
    public void TryResolve_RefusesAnAbsolutePathElsewhere()
    {
        string outside = Path.Combine(Path.GetTempPath(), "elsewhere.txt");
        Assert.False(FileRead.TryResolve(this._root, outside, out _, out _));
    }

    [Fact]
    public void TryResolve_SaysWhatIsWrongWhenTheFileIsSimplyAbsent()
    {
        Assert.False(FileRead.TryResolve(this._root, "missing.txt", out _, out string problem));
        Assert.Contains("not in the project folder", problem);
    }

    [Fact]
    public void ReadText_ReturnsASliceAndSaysWhenThereIsMore()
    {
        this.Write("long.txt", new string('x', 100));

        Assert.True(FileRead.ReadText(this._root, "long.txt", 0, 40)
            .IsOk(out FileTextResult? first, out _));
        Assert.Equal(40, first!.Text.Length);
        Assert.Equal(100, first.TotalChars);
        Assert.True(first.HasMore);

        Assert.True(FileRead.ReadText(this._root, "long.txt", first.End, 1000)
            .IsOk(out FileTextResult? rest, out _));
        Assert.Equal(60, rest!.Text.Length);
        Assert.False(rest.HasMore);
    }

    [Fact]
    public void ReadText_RefusesABinaryFileAndSaysWhatItIs()
    {
        // A LAS file read as text is replacement characters — which looks like an empty or broken
        // file, and is the wrong conclusion about a perfectly good point cloud.
        this.WriteBytes("tile.las", new byte[] { 0x4C, 0x41, 0x53, 0x46, 0, 0, 0, 1, 2, 3 });

        Assert.False(FileRead.ReadText(this._root, "tile.las").IsOk(out _, out string? error));
        Assert.Contains("not a text file", error);
        Assert.Contains("LAS point cloud", error);
    }

    [Fact]
    public void Search_ReportsLineNumbers()
    {
        this.Write("index.csv", "id,name\n1,north tile\n2,south tile\n3,NORTH annex\n");

        Assert.True(FileRead.Search(this._root, "index.csv", "north")
            .IsOk(out IReadOnlyList<FileMatch>? matches, out _));

        Assert.Equal(2, matches!.Count);
        Assert.Equal(2, matches[0].Line);
        Assert.Equal(4, matches[1].Line);
    }

    [Fact]
    public void Search_NeedsAQuery()
    {
        this.Write("a.txt", "x");
        Assert.False(FileRead.Search(this._root, "a.txt", string.Empty).IsOk(out _, out _));
    }

    [Fact]
    public void Stat_DescribesWithoutReading()
    {
        this.WriteBytes("doc.pdf", Encoding.ASCII.GetBytes("%PDF-1.7 and then some bytes"));

        Assert.True(FileRead.Stat(this._root, "doc.pdf").IsOk(out FileDescription? file, out _));
        Assert.Equal("doc.pdf", file!.RelativePath);
        Assert.False(file.IsText);
        Assert.Equal("PDF", file.Format);
        Assert.True(Path.IsPathRooted(file.FullPath));
    }

    [Fact]
    public void Stat_OfATextFileSaysItIsReadable()
    {
        this.Write("notes.md", "# Site\n\nThe survey is in tile 4830E.");

        Assert.True(FileRead.Stat(this._root, "notes.md").IsOk(out FileDescription? file, out _));
        Assert.True(file!.IsText);
        Assert.Null(file.Format);
    }

    [Fact]
    public void NoProjectFolder_IsReportedRatherThanCrashing()
    {
        Assert.False(FileRead.List(null).IsOk(out _, out string? error));
        Assert.Contains("No project folder", error);
        Assert.False(FileRead.TryResolve(null, "a.txt", out _, out _));
    }
}
