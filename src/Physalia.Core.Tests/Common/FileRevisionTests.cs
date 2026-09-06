// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Physalia.Core.Api;
using Physalia.Core.Common;
using Xunit;

namespace Physalia.Core.Tests.Common;

/// <summary>
/// Covers the change stamp the config stores hand out, and that a store's own stamp actually moves
/// when it is written — which is what lets a node holding a cached read notice an edit.
/// </summary>
public class FileRevisionTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (string path in this._tempFiles)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void An_absent_file_stamps_as_none()
    {
        Assert.Equal("none", FileRevision.Stamp(this.TempPath()));
    }

    [Fact]
    public void A_null_or_blank_path_stamps_as_none()
    {
        Assert.Equal("none", FileRevision.Stamp(null));
        Assert.Equal("none", FileRevision.Stamp("   "));
    }

    [Fact]
    public void An_unchanged_file_keeps_its_stamp()
    {
        string path = this.TempPath();
        File.WriteAllText(path, "one");

        Assert.Equal(FileRevision.Stamp(path), FileRevision.Stamp(path));
    }

    [Fact]
    public void A_content_change_of_the_same_length_still_moves_the_stamp()
    {
        // Length alone would miss this, which is why the write time is in there too.
        string path = this.TempPath();
        File.WriteAllText(path, "one");
        string before = FileRevision.Stamp(path);

        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(1));

        Assert.NotEqual(before, FileRevision.Stamp(path));
    }

    [Fact]
    public void A_length_change_at_the_same_write_time_still_moves_the_stamp()
    {
        // And the write time alone would miss THIS, which is the case a coarse file-system clock
        // produces when a small file is saved twice in quick succession.
        string path = this.TempPath();
        File.WriteAllText(path, "one");
        DateTime when = File.GetLastWriteTimeUtc(path);
        string before = FileRevision.Stamp(path);

        File.WriteAllText(path, "one-longer");
        File.SetLastWriteTimeUtc(path, when);

        Assert.NotEqual(before, FileRevision.Stamp(path));
    }

    [Fact]
    public void Creating_a_file_moves_the_stamp_off_none()
    {
        // The case that made adding the FIRST endpoint work only by accident before: a node that
        // treats "absent" as a stamp like any other picks up the file appearing.
        string path = this.TempPath();
        Assert.Equal("none", FileRevision.Stamp(path));

        File.WriteAllText(path, "{}");

        Assert.NotEqual("none", FileRevision.Stamp(path));
    }

    [Fact]
    public void Saving_an_endpoint_moves_the_stores_stamp()
    {
        // The property the nodes actually depend on: edit in the chat window, and a node holding a
        // cached copy of the list sees a different stamp on its next solve.
        var store = new ApiEndpointStore(this.TempPath());
        store.Save(new ApiEndpoint("vancouver", "https://a.example.com/"));
        string before = store.RevisionStamp;

        store.Save(new ApiEndpoint("vancouver", "https://changed.example.com/"));

        Assert.NotEqual(before, store.RevisionStamp);
        Assert.Equal("https://changed.example.com/", store.Find("vancouver")!.BaseUrl);
    }

    private string TempPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"phy-rev-{Guid.NewGuid():N}.tmp");
        this._tempFiles.Add(path);
        return path;
    }
}
