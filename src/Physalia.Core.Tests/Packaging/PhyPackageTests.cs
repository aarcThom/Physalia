// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Physalia.Core.Common;
using Physalia.Core.Packaging;
using Xunit;

namespace Physalia.Core.Tests.Packaging;

public sealed class PhyPackageTests : IDisposable
{
    private readonly string _root;

    public PhyPackageTests()
    {
        this._root = Path.Combine(Path.GetTempPath(), "phy-tests-" + Guid.NewGuid().ToString("N"));
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

    private string Path_(string name) => Path.Combine(this._root, name);

    private static byte[] Doc(string marker) => Encoding.UTF8.GetBytes("GH-DOCUMENT:" + marker);

    [Fact]
    public void Write_ThenRead_RoundTripsTheManifestAndDocument()
    {
        string path = this.Path_("a.phy");
        var manifest = PhyManifest.For("curious-cake-soap-fun", "Reads LiDAR tiles.", "Ask me for a site.");

        Assert.True(PhyPackage.Write(path, manifest, Doc("x")).IsOk(out long bytes, out _));
        Assert.True(bytes > 0);

        Assert.True(PhyPackage.Read(path).IsOk(out PhyPackageContents? contents, out _));
        Assert.Equal("curious-cake-soap-fun", contents!.Manifest.Name);
        Assert.Equal("Reads LiDAR tiles.", contents.Manifest.Description);
        Assert.Equal("Ask me for a site.", contents.Manifest.ChatText);
        Assert.Equal(PhyManifest.CurrentFormatVersion, contents.Manifest.FormatVersion);
        Assert.Equal(Doc("x"), contents.DocumentBytes);
        Assert.Equal(0, contents.FileCount);
    }

    [Fact]
    public void Write_CarriesProjectFilesAndExtractsThemWithoutTheirPrefix()
    {
        string source = this.Path_("source");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        File.WriteAllText(Path.Combine(source, "notes.txt"), "hello");
        File.WriteAllText(Path.Combine(source, "sub", "index.json"), "{}");

        string path = this.Path_("b.phy");
        var files = new List<PhyPackageFile>
        {
            new("notes.txt", Path.Combine(source, "notes.txt")),
            new("sub/index.json", Path.Combine(source, "sub", "index.json")),
        };

        Assert.True(PhyPackage.Write(path, PhyManifest.For("n", null, null), Doc("y"), files).IsOk(out _, out _));
        Assert.True(PhyPackage.Read(path).IsOk(out PhyPackageContents? contents, out _));
        Assert.Equal(2, contents!.FileCount);

        string destination = this.Path_("out");
        Assert.True(PhyPackage.ExtractFiles(path, destination).IsOk(out ZipExtractSummary? summary, out string? err), err);

        // The files/ prefix belongs to the package, not to the project folder.
        Assert.Equal(2, summary!.Files.Count);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(destination, "notes.txt")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(destination, "sub", "index.json")));
        Assert.False(Directory.Exists(Path.Combine(destination, "files")));
    }

    [Fact]
    public void ExtractFiles_LeavesTheManifestAndDocumentBehind()
    {
        string path = this.Path_("c.phy");
        Assert.True(PhyPackage.Write(path, PhyManifest.For("n", null, null), Doc("z")).IsOk(out _, out _));

        string destination = this.Path_("out-c");
        Assert.True(PhyPackage.ExtractFiles(path, destination).IsOk(out ZipExtractSummary? summary, out _));

        Assert.Empty(summary!.Files);
        Assert.False(File.Exists(Path.Combine(destination, PhyPackage.ManifestEntry)));
        Assert.False(File.Exists(Path.Combine(destination, PhyPackage.DocumentEntry)));
    }

    [Fact]
    public void Write_SkipsAFileThatHasGoneMissingRatherThanFailing()
    {
        string path = this.Path_("d.phy");
        var files = new List<PhyPackageFile> { new("gone.txt", this.Path_("nowhere/gone.txt")) };

        Assert.True(PhyPackage.Write(path, PhyManifest.For("n", null, null), Doc("q"), files).IsOk(out _, out _));
        Assert.True(PhyPackage.Read(path).IsOk(out PhyPackageContents? contents, out _));
        Assert.Equal(0, contents!.FileCount);
    }

    [Fact]
    public void IsPackage_DistinguishesAZipFromAPlainGhArchive()
    {
        string package = this.Path_("e.phy");
        Assert.True(PhyPackage.Write(package, PhyManifest.For("n", null, null), Doc("k")).IsOk(out _, out _));

        string plain = this.Path_("legacy.gh");
        File.WriteAllBytes(plain, new byte[] { 0x47, 0x48, 0x58, 0x20 });

        Assert.True(PhyPackage.IsPackage(package));
        Assert.False(PhyPackage.IsPackage(plain));
        Assert.False(PhyPackage.IsPackage(this.Path_("missing.phy")));
    }

    [Fact]
    public void Read_RefusesAZipThatIsNotAPackage()
    {
        string path = this.Path_("random.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntry("readme.txt");
        }

        Assert.True(PhyPackage.IsPackage(path));
        Assert.False(PhyPackage.Read(path).IsOk(out _, out string? error));
        Assert.Contains("manifest.json", error);
    }

    [Fact]
    public void Read_RefusesAFutureFormatRatherThanGuessing()
    {
        string path = this.Path_("future.phy");
        var manifest = new PhyManifest(
            PhyManifest.CurrentFormatVersion + 1,
            "n",
            null,
            null,
            DateTimeOffset.UtcNow,
            Array.Empty<PhyDownloadRecord>());

        Assert.True(PhyPackage.Write(path, manifest, Doc("f")).IsOk(out _, out _));
        Assert.False(PhyPackage.Read(path).IsOk(out _, out string? error));
        Assert.Contains("newer Physalia", error);
    }

    [Fact]
    public void DownloadLedger_RoundTrips()
    {
        string path = this.Path_("ledger.phy");
        var downloads = new List<PhyDownloadRecord>
        {
            new("https://example.org/tile.las", "tile.las", 421_000_000),
        };

        var manifest = PhyManifest.For("n", null, null, downloads);
        Assert.True(PhyPackage.Write(path, manifest, Doc("l")).IsOk(out _, out _));
        Assert.True(PhyPackage.Read(path).IsOk(out PhyPackageContents? contents, out _));

        PhyDownloadRecord record = Assert.Single(contents!.Manifest.Downloads);
        Assert.Equal("https://example.org/tile.las", record.Url);
        Assert.Equal("tile.las", record.File);
        Assert.Equal(421_000_000, record.Bytes);
    }

    [Fact]
    public void ReadManifest_DoesNotNeedTheDocument()
    {
        // The gallery calls this on every file it lists, so it must not depend on the expensive half.
        string path = this.Path_("gallery.phy");
        Assert.True(PhyPackage.Write(path, PhyManifest.For("shown-in-gallery", "why", null), Doc("g")).IsOk(out _, out _));

        Assert.True(PhyPackage.ReadManifest(path).IsOk(out PhyManifest? manifest, out _));
        Assert.Equal("shown-in-gallery", manifest!.Name);
        Assert.Equal("why", manifest.Description);
    }

    [Fact]
    public void For_NormalizesBlankTextToNull()
    {
        PhyManifest manifest = PhyManifest.For("n", "   ", string.Empty);

        Assert.Null(manifest.Description);
        Assert.Null(manifest.ChatText);
    }
}
