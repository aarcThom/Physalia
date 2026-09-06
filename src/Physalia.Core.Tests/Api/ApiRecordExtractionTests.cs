// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Physalia.Core.Api;
using Xunit;

namespace Physalia.Core.Tests.Api;

/// <summary>
/// Covers what actually reaches the Grasshopper canvas: one item per record, unwrapped and joined
/// across pages.
/// </summary>
/// <remarks>
/// Handing over the raw bodies made the consumer unwrap each envelope, know which key that API puts
/// its rows under, and concatenate — and the shape changed with the result size, so a script written
/// against a one-page test query broke on the real multi-page one. These tests pin the shape being
/// uniform.
/// </remarks>
public class ApiRecordExtractionTests
{
    [Fact]
    public void Records_are_unwrapped_from_the_envelope()
    {
        (IReadOnlyList<string> items, bool areRecords) = ApiResponseSummary.ExtractRecords(new[] { Page(0, 3, 3) });

        Assert.True(areRecords);
        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal(JsonValueKind.Object, Kind(item)));
        Assert.DoesNotContain(items, item => item.Contains("total_count", StringComparison.Ordinal));
    }

    [Fact]
    public void Records_are_joined_across_pages_in_order()
    {
        // The whole point: sixty pages and one page produce the same shape, so downstream code does
        // not change when a query grows.
        (IReadOnlyList<string> items, bool areRecords) =
            ApiResponseSummary.ExtractRecords(new[] { Page(0, 3, 6), Page(3, 3, 6) });

        Assert.True(areRecords);
        Assert.Equal(6, items.Count);
        Assert.Equal(
            new[] { 0, 1, 2, 3, 4, 5 },
            items.Select(i => JsonDocument.Parse(i).RootElement.GetProperty("elevation").GetInt32()));
    }

    [Fact]
    public void A_one_page_read_has_the_same_shape_as_a_many_page_read()
    {
        (IReadOnlyList<string> single, bool singleAreRecords) = ApiResponseSummary.ExtractRecords(new[] { Page(0, 2, 2) });
        (IReadOnlyList<string> many, bool manyAreRecords) =
            ApiResponseSummary.ExtractRecords(new[] { Page(0, 1, 2), Page(1, 1, 2) });

        Assert.Equal(singleAreRecords, manyAreRecords);
        Assert.Equal(single.Count, many.Count);
    }

    [Fact]
    public void A_bare_array_response_is_unwrapped_too()
    {
        (IReadOnlyList<string> items, bool areRecords) =
            ApiResponseSummary.ExtractRecords(new[] { "[{\"id\":1},{\"id\":2}]" });

        Assert.True(areRecords);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void A_response_with_no_record_collection_falls_back_to_the_whole_body()
    {
        // A single-document response — dataset metadata, say — is not a record list, and the caller
        // that asked for a document should get the document.
        const string body = "{\"dataset_id\":\"trees\",\"records_count\":2093}";

        (IReadOnlyList<string> items, bool areRecords) = ApiResponseSummary.ExtractRecords(new[] { body });

        Assert.False(areRecords);
        Assert.Equal(new[] { body }, items);
    }

    [Fact]
    public void A_non_json_body_falls_back_rather_than_vanishing()
    {
        const string csv = "elevation,geom\n12,LINESTRING(...)";

        (IReadOnlyList<string> items, bool areRecords) = ApiResponseSummary.ExtractRecords(new[] { csv });

        Assert.False(areRecords);
        Assert.Equal(new[] { csv }, items);
    }

    [Fact]
    public void An_empty_record_list_falls_back_to_the_body()
    {
        // Nothing to hand over as records, so the body — which may carry an error message or a
        // total_count of zero — is more useful than an empty list saying nothing.
        (IReadOnlyList<string> items, bool areRecords) = ApiResponseSummary.ExtractRecords(new[] { Page(0, 0, 0) });

        Assert.False(areRecords);
        Assert.Single(items);
    }

    [Fact]
    public void Nothing_gathered_yields_nothing()
    {
        (IReadOnlyList<string> items, bool areRecords) = ApiResponseSummary.ExtractRecords(Array.Empty<string>());

        Assert.Empty(items);
        Assert.False(areRecords);

        Assert.Empty(ApiResponseSummary.ExtractRecords(null).Items);
    }

    [Fact]
    public void An_unreadable_later_page_is_skipped_not_mixed_in()
    {
        // Returning records for the good pages and a raw body for the bad one would hand downstream
        // a list whose items are two different kinds of thing, indistinguishable to a parser.
        (IReadOnlyList<string> items, bool areRecords) =
            ApiResponseSummary.ExtractRecords(new[] { Page(0, 2, 4), "not json at all" });

        Assert.True(areRecords);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void The_record_count_matches_what_the_pager_counted()
    {
        // ExtractRecords and CountRecords share RecordKeys, so the number on the wire and the number
        // in the model's summary can never disagree.
        string[] pages = { Page(0, 100, 145), Page(100, 45, 145) };

        int counted = pages.Sum(p => ApiResponseSummary.CountRecords(p).Count);

        Assert.Equal(counted, ApiResponseSummary.ExtractRecords(pages).Items.Count);
    }

    private static JsonValueKind Kind(string json) => JsonDocument.Parse(json).RootElement.ValueKind;

    private static string Page(int start, int records, int total)
    {
        string rows = string.Join(
            ",",
            Enumerable.Range(start, records).Select(i => $"{{\"elevation\":{i},\"geom\":null}}"));

        return $"{{\"total_count\":{total},\"results\":[{rows}]}}";
    }
}
