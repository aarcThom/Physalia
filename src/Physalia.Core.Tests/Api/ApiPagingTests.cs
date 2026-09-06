// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Physalia.Core.Api;
using Xunit;

namespace Physalia.Core.Tests.Api;

/// <summary>
/// Covers the pieces the paged read is built out of — offset rewriting, counting a page, and how a
/// gathered set is described — plus the rule that a partial read must say so.
/// </summary>
public class ApiPagingTests
{
    [Fact]
    public void An_offset_is_added_to_a_query_that_has_none()
    {
        Assert.Equal("limit=100&offset=200", ApiRequest.WithOffset("limit=100", 200));
    }

    [Fact]
    public void An_existing_offset_is_replaced_not_duplicated()
    {
        // Two offsets in one query string leaves which one wins up to the API, which is not a
        // decision worth delegating.
        string result = ApiRequest.WithOffset("limit=100&offset=0&select=elevation", 300);

        Assert.Equal("limit=100&select=elevation&offset=300", result);
        Assert.Single(result.Split('&').Where(p => p.StartsWith("offset=", StringComparison.Ordinal)));
    }

    [Fact]
    public void An_offset_replacement_is_case_insensitive()
    {
        Assert.Equal("offset=50", ApiRequest.WithOffset("OFFSET=0", 50));
    }

    [Fact]
    public void An_empty_query_becomes_just_the_offset()
    {
        Assert.Equal("offset=100", ApiRequest.WithOffset(null, 100));
        Assert.Equal("offset=100", ApiRequest.WithOffset("?", 100));
    }

    [Fact]
    public void A_page_reports_its_own_count_and_the_total_matched()
    {
        // The count is what the pager strides by — read back rather than assumed, so an API capping
        // pages at 20 or 50 walks correctly without being told which.
        (int count, int? total) = ApiResponseSummary.CountRecords(Page(records: 20, total: 145));

        Assert.Equal(20, count);
        Assert.Equal(145, total);
    }

    [Fact]
    public void A_bare_array_counts_and_reports_no_total()
    {
        (int count, int? total) = ApiResponseSummary.CountRecords("[{\"id\":1},{\"id\":2}]");

        Assert.Equal(2, count);
        Assert.Null(total);
    }

    [Fact]
    public void A_non_json_body_counts_as_nothing_rather_than_throwing()
    {
        // An endpoint answering CSV is a fine endpoint; it simply cannot be walked, and the pager
        // stops after one request rather than failing the call.
        Assert.Equal((0, null), ApiResponseSummary.CountRecords("id,elevation\n1,12"));
        Assert.Equal((0, null), ApiResponseSummary.CountRecords(null));
    }

    [Fact]
    public void A_complete_single_page_read_is_summarised_as_itself()
    {
        // No paging commentary when there is nothing to say about paging.
        var response = new ApiPagedResponse(new[] { Page(2, 2) }, 2, 2, null);

        Assert.Equal(ApiResponseSummary.Summarize(Page(2, 2), 4000), ApiResponseSummary.Summarize(response, 4000));
    }

    [Fact]
    public void A_gathered_set_reports_the_whole_set_not_the_last_page()
    {
        // The failure this exists to prevent: the model reasons about the final page while the canvas
        // receives every page, and nothing in a page's own shape reveals the difference.
        var response = new ApiPagedResponse(
            new[] { Page(100, 145), Page(45, 145) },
            145,
            145,
            null,
            CanPage: true);

        string summary = ApiResponseSummary.Summarize(response, 4000);

        Assert.Contains("Gathered 145 records over 2 requests", summary, StringComparison.Ordinal);
        Assert.Contains("Response output", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partial_read_says_so_and_says_why()
    {
        var response = new ApiPagedResponse(
            new[] { Page(100, 5929) },
            100,
            5929,
            "stopped at the 100-record limit set on the node; 5929 matched in total",
            CanPage: true);

        string summary = ApiResponseSummary.Summarize(response, 4000);

        Assert.Contains("THIS IS NOT THE WHOLE RESULT SET", summary, StringComparison.Ordinal);
        Assert.Contains("5929 matched in total", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_read_is_partial_even_when_nothing_went_wrong()
    {
        // Fewer records than matched is partial on its own — the walk ending tidily is not the same
        // as the walk being complete.
        var response = new ApiPagedResponse(new[] { Page(100, 145) }, 100, 145, null, CanPage: true);

        Assert.True(response.IsPartial);
        Assert.Contains("THIS IS NOT THE WHOLE RESULT SET", ApiResponseSummary.Summarize(response, 4000), StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_read_from_an_UNPAGED_endpoint_names_the_setting_that_fixes_it()
    {
        // The dead end this exists to prevent: told only that it received less than matched, a reader
        // concludes the NODE is capped and reports that the pipeline needs rebuilding — when the
        // remedy is one dropdown nobody knew was unset. Observed live, 2026-09-05.
        var response = new ApiPagedResponse(new[] { Page(100, 811) }, 100, 811, null, CanPage: false);

        string summary = ApiResponseSummary.Summarize(response, 4000);

        Assert.Contains("no paging configured", summary, StringComparison.Ordinal);
        Assert.Contains("API calls page", summary, StringComparison.Ordinal);
        Assert.Contains("pipeline itself needs no change", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_read_from_a_PAGED_endpoint_does_not_blame_the_setting()
    {
        // It is set correctly; saying otherwise would send the user to change something that is fine.
        var response = new ApiPagedResponse(new[] { Page(100, 811) }, 100, 811, null, CanPage: true);

        Assert.DoesNotContain("no paging configured", ApiResponseSummary.Summarize(response, 4000), StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_read_is_not_partial()
    {
        Assert.False(new ApiPagedResponse(new[] { Page(45, 45) }, 45, 45, null).IsPartial);
    }

    [Fact]
    public void A_read_with_no_reported_total_is_not_assumed_partial()
    {
        // An API that reports no total tells us nothing about what is left; guessing "incomplete"
        // would put a warning on every complete read from such an endpoint.
        Assert.False(new ApiPagedResponse(new[] { "[]" }, 0, null, null).IsPartial);
    }

    [Fact]
    public void An_empty_gather_is_reported_plainly()
    {
        var response = new ApiPagedResponse(Array.Empty<string>(), 0, null, null);

        Assert.Equal("The API returned nothing.", ApiResponseSummary.Summarize(response, 4000));
    }

    [Fact]
    public void Paging_defaults_to_none_on_an_endpoint()
    {
        // Guessing wrong is not a no-op — a cursor API given offsets returns page one forever — so
        // the safe style is the one you get without saying anything.
        Assert.Equal(ApiPaging.None, new ApiEndpoint("x", "https://example.com/").Paging);
    }

    private static string Page(int records, int total)
    {
        string rows = string.Join(
            ",",
            Enumerable.Range(0, records).Select(i => $"{{\"elevation\":{i},\"geom\":null}}"));

        return $"{{\"total_count\":{total},\"results\":[{rows}]}}";
    }
}
