// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Linq;
using Physalia.Core.Api;
using Xunit;

namespace Physalia.Core.Tests.Api;

/// <summary>
/// Covers what the model is handed back when a response is too big to give it whole.
/// </summary>
public class ApiResponseSummaryTests
{
    [Fact]
    public void A_body_within_budget_comes_back_untouched()
    {
        const string body = "{\"results\":[{\"id\":1}]}";

        Assert.Equal(body, ApiResponseSummary.Summarize(body, 4000));
    }

    [Fact]
    public void A_paged_response_reports_the_total_not_just_the_page()
    {
        // The point of summarising rather than truncating: a blind cut gives the model the first few
        // records and no hint that more matched, which is how it concludes a query returned
        // everything when it returned one page.
        string body = Paged(pageSize: 30, totalCount: 4821);

        string summary = ApiResponseSummary.Summarize(body, 400);

        Assert.Contains("30 records", summary, StringComparison.Ordinal);
        Assert.Contains("4821 matching in total", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_summary_names_the_fields_and_shows_one_record()
    {
        string body = Paged(pageSize: 30, totalCount: 30);

        string summary = ApiResponseSummary.Summarize(body, 400);

        Assert.Contains("Fields: id, species, height", summary, StringComparison.Ordinal);
        Assert.Contains("First record:", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_summary_says_where_the_rest_of_the_data_went()
    {
        string summary = ApiResponseSummary.Summarize(Paged(30, 30), 400);

        Assert.Contains("Response output", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_array_is_summarised_too()
    {
        string body = "[" + string.Join(",", Enumerable.Range(0, 200).Select(i => $"{{\"id\":{i}}}")) + "]";

        string summary = ApiResponseSummary.Summarize(body, 200);

        Assert.Contains("200 records", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_json_body_is_truncated_rather_than_refused()
    {
        // An API answering with CSV or prose is still readable; refusing to summarise it would make
        // the tool useless for exactly the endpoints nobody designed for.
        string body = new string('x', 5000);

        string summary = ApiResponseSummary.Summarize(body, 100);

        Assert.StartsWith(new string('x', 100), summary, StringComparison.Ordinal);
        Assert.Contains("truncated 4900 characters", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void An_over_budget_json_object_with_no_records_falls_back_to_truncation()
    {
        string body = "{\"message\":\"" + new string('y', 5000) + "\"}";

        Assert.Contains("truncated", ApiResponseSummary.Summarize(body, 100), StringComparison.Ordinal);
    }

    [Fact]
    public void Field_names_are_read_off_the_first_record()
    {
        Assert.Equal(
            new[] { "id", "species", "height" },
            ApiResponseSummary.FieldNames(Paged(2, 2)));
    }

    [Fact]
    public void Field_names_of_an_unreadable_body_are_empty()
    {
        Assert.Empty(ApiResponseSummary.FieldNames("not json at all"));
        Assert.Empty(ApiResponseSummary.FieldNames(null));
    }

    private static string Paged(int pageSize, int totalCount)
    {
        string records = string.Join(
            ",",
            Enumerable.Range(0, pageSize)
                .Select(i => $"{{\"id\":{i},\"species\":\"Acer\",\"height\":{i * 2}}}"));

        return $"{{\"total_count\":{totalCount},\"results\":[{records}]}}";
    }
}
