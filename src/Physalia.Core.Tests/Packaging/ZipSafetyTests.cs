// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Physalia.Core.Common;
using Physalia.Core.Packaging;
using Xunit;

namespace Physalia.Core.Tests.Packaging;

public sealed class ZipSafetyTests : IDisposable
{
    private readonly string _root;

    public ZipSafetyTests()
    {
        this._root = Path.Combine(Path.GetTempPath(), "zip-tests-" + Guid.NewGuid().ToString("N"));
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

    private string Destination => Path.Combine(this._root, "dest");

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("sub/notes.txt")]
    [InlineData("sub\\notes.txt")]
    [InlineData("/leading-slash.txt")]
    [InlineData("a/../b.txt")]
    public void TryResolveEntryPath_AcceptsNamesThatStayInside(string entry)
    {
        Assert.True(ZipSafety.TryResolveEntryPath(this.Destination, entry, out string full, out string error), error);
        Assert.StartsWith(Path.GetFullPath(this.Destination), full, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("../../../Startup/run.bat")]
    [InlineData("sub/../../escape.txt")]
    [InlineData("sub\\..\\..\\escape.txt")]
    public void TryResolveEntryPath_RefusesAnythingThatClimbsOut(string entry)
    {
        Assert.False(ZipSafety.TryResolveEntryPath(this.Destination, entry, out _, out string error));
        Assert.Contains("outside", error);
    }

    [Fact]
    public void TryResolveEntryPath_RefusesAnAbsolutePath()
    {
        string absolute = Path.Combine(Path.GetTempPath(), "elsewhere.txt");

        // A rooted name resolves away from the destination entirely, which is exactly what checking
        // the RESOLVED path catches and a scan for ".." would not.
        Assert.False(ZipSafety.TryResolveEntryPath(this.Destination, absolute, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveEntryPath_RefusesANamelessEntry(string entry)
    {
        Assert.False(ZipSafety.TryResolveEntryPath(this.Destination, entry, out _, out _));
    }

    [Fact]
    public void ExtractTo_WritesFilesAndCreatesFolders()
    {
        string path = this.Zip(("a.txt", "one"), ("nested/b.txt", "two"));

        using ZipArchive archive = ZipFile.OpenRead(path);
        Assert.True(ZipSafety.ExtractTo(archive, this.Destination, ZipExtractLimits.Default)
            .IsOk(out ZipExtractSummary? summary, out string? error), error);

        Assert.Equal(2, summary!.Files.Count);
        Assert.Equal(6, summary.TotalBytes);
        Assert.Equal("one", File.ReadAllText(Path.Combine(this.Destination, "a.txt")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(this.Destination, "nested", "b.txt")));
    }

    [Fact]
    public void ExtractTo_StopsAtAnEntryThatWouldEscape()
    {
        string path = this.Zip(("safe.txt", "ok"), ("../escape.txt", "bad"));

        using ZipArchive archive = ZipFile.OpenRead(path);
        Assert.False(ZipSafety.ExtractTo(archive, this.Destination, ZipExtractLimits.Default)
            .IsOk(out _, out string? error));

        Assert.Contains("outside", error);
        Assert.False(File.Exists(Path.Combine(this._root, "escape.txt")));
    }

    [Fact]
    public void ExtractTo_CountsBytesAsTheyLandRatherThanTrustingTheHeader()
    {
        string path = this.Zip(("big.txt", new string('x', 4096)));

        using ZipArchive archive = ZipFile.OpenRead(path);
        var limits = new ZipExtractLimits(MaxEntries: 10, MaxTotalBytes: 1024, MaxEntryBytes: 1024);

        Assert.False(ZipSafety.ExtractTo(archive, this.Destination, limits).IsOk(out _, out string? error));
        Assert.Contains("expands past", error);
    }

    [Fact]
    public void ExtractTo_RefusesMoreEntriesThanTheLimitAllows()
    {
        string path = this.Zip(("a.txt", "1"), ("b.txt", "2"), ("c.txt", "3"));

        using ZipArchive archive = ZipFile.OpenRead(path);
        var limits = new ZipExtractLimits(MaxEntries: 2);

        Assert.False(ZipSafety.ExtractTo(archive, this.Destination, limits).IsOk(out _, out string? error));
        Assert.Contains("more than 2 files", error);
    }

    [Fact]
    public void ExtractTo_MapsAndSkipsEntriesButStillContainsThem()
    {
        string path = this.Zip(("keep/a.txt", "one"), ("drop/b.txt", "two"), ("keep/../../out.txt", "bad"));

        using ZipArchive archive = ZipFile.OpenRead(path);
        Assert.False(ZipSafety.ExtractTo(
                archive,
                this.Destination,
                ZipExtractLimits.Default,
                entry => entry.FullName.StartsWith("keep/", StringComparison.Ordinal)
                    ? entry.FullName.Substring("keep/".Length)
                    : null)
            .IsOk(out _, out string? error));

        // The mapped name still climbs out, so mapping is not a way past the guard.
        Assert.Contains("outside", error);
        Assert.Equal("one", File.ReadAllText(Path.Combine(this.Destination, "a.txt")));
        Assert.False(File.Exists(Path.Combine(this.Destination, "b.txt")));
    }

    private string Zip(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(this._root, "in-" + Guid.NewGuid().ToString("N") + ".zip");

        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach ((string name, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using Stream target = entry.Open();
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            target.Write(bytes, 0, bytes.Length);
        }

        return path;
    }
}
