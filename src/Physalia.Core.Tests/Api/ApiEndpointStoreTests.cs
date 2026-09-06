// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Physalia.Core.Api;
using Xunit;

namespace Physalia.Core.Tests.Api;

/// <summary>
/// Covers the plain-JSON store behind the chat window's "API calls" page.
/// </summary>
public class ApiEndpointStoreTests : IDisposable
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
    public void An_absent_store_reads_as_empty()
    {
        Assert.Empty(this.NewStore().Read());
    }

    [Fact]
    public void An_unparseable_store_reads_as_empty_rather_than_throwing()
    {
        string path = this.TempPath();
        File.WriteAllText(path, "{ this is not json");

        Assert.Empty(new ApiEndpointStore(path).Read());
    }

    [Fact]
    public void A_saved_endpoint_round_trips_every_field()
    {
        ApiEndpointStore store = this.NewStore();
        store.Save(new ApiEndpoint(
            "vancouver",
            "https://opendata.vancouver.ca/api/explore/v2.1/",
            ApiAuth.CustomHeader,
            "Authorization",
            "Apikey ",
            "VANCOUVER_API_KEY"));

        ApiEndpoint entry = Assert.Single(store.Read());

        Assert.Equal("vancouver", entry.Name);
        Assert.Equal("https://opendata.vancouver.ca/api/explore/v2.1/", entry.BaseUrl);
        Assert.Equal(ApiAuth.CustomHeader, entry.Auth);
        Assert.Equal("Authorization", entry.AuthName);
        Assert.Equal("Apikey ", entry.AuthPrefix);
        Assert.Equal("VANCOUVER_API_KEY", entry.EnvVar);
    }

    [Fact]
    public void Find_is_case_insensitive_and_misses_cleanly()
    {
        ApiEndpointStore store = this.NewStore();
        store.Save(new ApiEndpoint("Vancouver", "https://example.com/"));

        Assert.NotNull(store.Find("vancouver"));
        Assert.Null(store.Find("seattle"));
        Assert.Null(store.Find(null));
    }

    [Fact]
    public void An_edit_keeps_the_row_where_it_was()
    {
        // The list is what the setup page shows; an edit silently moving a row to the bottom reads
        // as a bug.
        ApiEndpointStore store = this.NewStore();
        store.Save(new ApiEndpoint("first", "https://a.example.com/"));
        store.Save(new ApiEndpoint("second", "https://b.example.com/"));
        store.Save(new ApiEndpoint("third", "https://c.example.com/"));

        store.Save(new ApiEndpoint("second", "https://changed.example.com/"));

        Assert.Equal(new[] { "first", "second", "third" }, store.Read().Select(e => e.Name));
        Assert.Equal("https://changed.example.com/", store.Find("second")!.BaseUrl);
    }

    [Fact]
    public void A_rename_drops_the_old_key_and_keeps_the_position()
    {
        ApiEndpointStore store = this.NewStore();
        store.Save(new ApiEndpoint("first", "https://a.example.com/"));
        store.Save(new ApiEndpoint("second", "https://b.example.com/"));

        store.Save(new ApiEndpoint("renamed", "https://b.example.com/"), replacing: "second");

        Assert.Equal(new[] { "first", "renamed" }, store.Read().Select(e => e.Name));
        Assert.Null(store.Find("second"));
    }

    [Fact]
    public void Remove_takes_the_entry_out_and_ignores_an_unknown_name()
    {
        ApiEndpointStore store = this.NewStore();
        store.Save(new ApiEndpoint("gone", "https://a.example.com/"));

        store.Remove("nothing-by-that-name");
        Assert.Single(store.Read());

        store.Remove("gone");
        Assert.Empty(store.Read());
    }

    [Fact]
    public void The_credential_id_is_namespaced_so_it_cannot_collide_with_a_provider()
    {
        Assert.Equal("api:openai", new ApiEndpoint("openai", "https://example.com/").CredentialId);
    }

    [Fact]
    public void An_open_endpoint_needs_no_key()
    {
        Assert.False(new ApiEndpoint("open", "https://example.com/").NeedsKey);
        Assert.True(new ApiEndpoint("keyed", "https://example.com/", ApiAuth.BearerHeader).NeedsKey);
    }

    private ApiEndpointStore NewStore() => new(this.TempPath());

    private string TempPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"phy-api-{Guid.NewGuid():N}.json");
        this._tempFiles.Add(path);
        return path;
    }
}
