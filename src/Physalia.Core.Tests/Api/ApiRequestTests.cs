// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.Api;
using Physalia.Core.Common;
using Xunit;

namespace Physalia.Core.Tests.Api;

/// <summary>
/// Covers URL composition for the API Call tool — which is the whole security boundary of that
/// tool, since the model supplies the path and the query and nothing else.
/// </summary>
public class ApiRequestTests
{
    private static readonly ApiEndpoint Open =
        new("vancouver", "https://opendata.vancouver.ca/api/explore/v2.1/");

    [Fact]
    public void A_relative_path_resolves_beneath_the_base()
    {
        Uri uri = Compose(Open, "catalog/datasets", string.Empty, null);

        Assert.Equal(
            "https://opendata.vancouver.ca/api/explore/v2.1/catalog/datasets",
            uri.AbsoluteUri);
    }

    [Fact]
    public void A_base_without_a_trailing_slash_still_keeps_its_last_segment()
    {
        // Relative resolution would otherwise drop "v2.1" and resolve against ".../explore/".
        var endpoint = new ApiEndpoint("v", "https://example.com/api/explore/v2.1");

        Uri uri = Compose(endpoint, "catalog/datasets", string.Empty, null);

        Assert.Equal("https://example.com/api/explore/v2.1/catalog/datasets", uri.AbsoluteUri);
    }

    [Fact]
    public void A_leading_slash_does_not_escape_to_the_host_root()
    {
        Uri uri = Compose(Open, "/catalog/datasets", string.Empty, null);

        Assert.StartsWith("https://opendata.vancouver.ca/api/explore/v2.1/", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void A_whole_url_is_refused()
    {
        // The important one: new Uri(base, "https://elsewhere") quietly returns the other host, so
        // relative resolution on its own is not a containment guarantee.
        string error = Refusal(Open, "https://evil.example.com/steal", string.Empty, null);

        Assert.Contains("relative", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_protocol_relative_url_never_reaches_the_other_host()
    {
        // Deliberately not pinned to one outcome, because the two guards divide this case by
        // PLATFORM: on Windows "//host/path" parses as an absolute file:// UNC URI and is refused
        // outright, while elsewhere it is not absolute and the leading-slash trim turns it into an
        // ordinary path segment beneath the base. Either is fine; asserting whichever this machine
        // happens to do would make the test fail on the other one for no reason.
        Result<Uri, string> result = ApiRequest.ComposeUri(Open, "//evil.example.com/steal", string.Empty, null);

        if (!result.IsErr(out _, out Uri? uri))
        {
            Assert.Equal("opendata.vancouver.ca", uri!.Authority);
        }
    }

    [Fact]
    public void Climbing_above_the_configured_base_is_refused()
    {
        // Same host, so the authority check passes; the path check is what catches this.
        string error = Refusal(Open, "../../../admin", string.Empty, null);

        Assert.Contains("climbs above", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_base_that_is_not_http_is_refused()
    {
        string error = Refusal(new ApiEndpoint("bad", "file:///c:/secrets"), "x", string.Empty, null);

        Assert.Contains("base URL", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_model_query_is_carried_through()
    {
        Uri uri = Compose(Open, "catalog/datasets/trees/records", "limit=20&select=species", null);

        Assert.Equal("?limit=20&select=species", uri.Query);
    }

    [Fact]
    public void A_leading_question_mark_on_the_query_is_tolerated()
    {
        Uri uri = Compose(Open, "records", "?limit=5", null);

        Assert.Equal("?limit=5", uri.Query);
    }

    [Fact]
    public void A_query_key_is_appended_last_so_the_model_cannot_shadow_it()
    {
        var endpoint = new ApiEndpoint("keyed", "https://example.com/v1/", ApiAuth.QueryParameter, "apikey");

        Uri uri = Compose(endpoint, "records", "apikey=decoy&limit=1", "real-secret");

        Assert.EndsWith("apikey=real-secret", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_endpoint_puts_nothing_in_the_query()
    {
        var endpoint = new ApiEndpoint("keyed", "https://example.com/v1/", ApiAuth.BearerHeader);

        Uri uri = Compose(endpoint, "records", "limit=1", "real-secret");

        Assert.DoesNotContain("real-secret", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_masks_a_query_key()
    {
        var endpoint = new ApiEndpoint("keyed", "https://example.com/v1/", ApiAuth.QueryParameter, "apikey");
        Uri uri = Compose(endpoint, "records", "limit=1", "real-secret");

        string redacted = ApiRequest.Redact(endpoint, uri);

        Assert.DoesNotContain("real-secret", redacted, StringComparison.Ordinal);
        Assert.Contains("apikey=***", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_leaves_a_header_endpoint_alone()
    {
        var endpoint = new ApiEndpoint("keyed", "https://example.com/v1/", ApiAuth.BearerHeader);
        Uri uri = Compose(endpoint, "records", "limit=1", "real-secret");

        Assert.Equal(uri.AbsoluteUri, ApiRequest.Redact(endpoint, uri));
    }

    private static Uri Compose(ApiEndpoint endpoint, string path, string query, string? key)
    {
        Result<Uri, string> result = ApiRequest.ComposeUri(endpoint, path, query, key);
        Assert.False(result.IsErr(out string? error, out Uri? uri), error);
        return uri!;
    }

    private static string Refusal(ApiEndpoint endpoint, string path, string query, string? key)
    {
        Result<Uri, string> result = ApiRequest.ComposeUri(endpoint, path, query, key);
        Assert.True(result.IsErr(out string? error, out _), "Expected the path to be refused.");
        return error!;
    }
}
