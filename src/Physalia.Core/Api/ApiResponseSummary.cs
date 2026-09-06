// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using System.Text.Json;

namespace Physalia.Core.Api;

/// <summary>
/// Shapes an API response for the model, when the whole body is too big to hand over.
/// </summary>
/// <remarks>
/// <para>The full body goes onto the node's Response output regardless — that is the wire the
/// definition consumes, and it is not charged to the conversation. What the model needs back is
/// enough to write its NEXT call: how many records matched, what the fields are called, and one
/// record to see the shape of. A blind truncation gives it the first few records and no idea that
/// more exist, which is how a model concludes a query returned everything when it returned a page.
/// </para>
/// <para>Falling back to a plain truncation is deliberate rather than an admission of defeat: an API
/// that answers with XML, CSV or prose is still readable, and refusing to summarise it would make
/// the tool useless for exactly the endpoints nobody thought to design for.</para>
/// </remarks>
public static class ApiResponseSummary
{
    // Where a paged API puts its rows. Checked in order; the first that is an array wins.
    private static readonly string[] RecordKeys = { "results", "records", "data", "items", "features", "value" };

    // Where a paged API puts the size of the whole match, as opposed to this page.
    private static readonly string[] CountKeys = { "total_count", "totalCount", "count", "total", "numberMatched" };

    /// <summary>
    /// Returns the body if it fits, or a compact description of it if it does not.
    /// </summary>
    /// <param name="body">The raw response body.</param>
    /// <param name="maxChars">The budget for what goes back to the model.</param>
    /// <returns>The body, or a summary of it.</returns>
    public static string Summarize(string? body, int maxChars)
    {
        string text = body ?? string.Empty;
        int budget = maxChars <= 0 ? 4000 : maxChars;

        if (text.Length <= budget)
            return text;

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            string? summary = TrySummarizeJson(document.RootElement, text.Length, budget);
            if (summary is not null)
                return summary;
        }
        catch (JsonException)
        {
            // Not JSON. Truncation below is the honest answer.
        }

        return Truncate(text, budget);
    }

    /// <summary>
    /// Describes a whole paged read for the model.
    /// </summary>
    /// <remarks>
    /// <b>It must describe the SET, not the last page.</b> Summarising only the final body is how a
    /// model reasons about 45 records while the canvas receives 145 — and it has no way to notice,
    /// because a page looks exactly like a complete answer. Anything left behind is stated outright
    /// for the same reason.
    /// </remarks>
    /// <param name="response">The gathered pages.</param>
    /// <param name="maxChars">The budget for what goes back to the model.</param>
    /// <returns>A description of everything gathered.</returns>
    public static string Summarize(ApiPagedResponse response, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Pages.Count == 0)
            return "The API returned nothing.";

        // One page and nothing withheld is the ordinary case, and the single-body summary already
        // says everything true about it. Wrapping that in paging commentary would be noise.
        if (response.Pages.Count == 1 && !response.IsPartial)
            return Summarize(response.Pages[0], maxChars);

        var sb = new StringBuilder();
        sb.Append("Gathered ").Append(response.RecordCount)
          .Append(response.RecordCount == 1 ? " record" : " records")
          .Append(" over ").Append(response.Pages.Count)
          .Append(response.Pages.Count == 1 ? " request" : " requests");

        if (response.MatchedCount is { } matched)
            sb.Append(", out of ").Append(matched).Append(" matching in total");

        sb.AppendLine(".");

        if (response.IsPartial)
        {
            sb.Append("THIS IS NOT THE WHOLE RESULT SET");
            if (response.StoppedBecause is { } why)
                sb.Append(" — ").Append(why);
            sb.AppendLine(".");
            sb.AppendLine("Narrow the query if you need all of it; do not present this as complete.");
        }

        sb.AppendLine();
        sb.AppendLine("Every record gathered is on this node's Response output, ONE ITEM PER RECORD");
        sb.AppendLine("in order — already unwrapped, so downstream code parses each item on its own and");
        sb.AppendLine("never has to know about pages or envelopes.");
        sb.AppendLine();
        sb.AppendLine("The first page looked like this:");

        int spent = sb.Length;
        sb.Append(Summarize(response.Pages[0], Math.Max(300, maxChars - spent)));

        return sb.ToString();
    }

    /// <summary>
    /// Unpacks gathered pages into the individual records inside them.
    /// </summary>
    /// <remarks>
    /// <para><b>One item per RECORD, not per page.</b> Handing the raw bodies over made the consumer
    /// do three things before it could touch the data: unwrap each page's envelope, know which key
    /// that particular API puts its rows under, and concatenate. Worse, the shape CHANGED with the
    /// result size — a one-page answer looked like a single document and a nine-page answer did not —
    /// so code written against a small test query broke on the real one. Records are uniform whatever
    /// the query returns, and there is no envelope left to get wrong.</para>
    /// <para>This costs nothing to know, because the paging walk already has to locate the rows to
    /// measure its own stride. What it does NOT do is merge envelopes: two disagreeing
    /// <c>total_count</c> values have no correct resolution, and the counts are reported separately
    /// anyway.</para>
    /// <para><b>An API with no record collection falls back to one item per body</b> — a single-object
    /// response, or anything that is not JSON at all. Uniformity within one call is what matters; a
    /// caller that got a document gets the document.</para>
    /// </remarks>
    /// <param name="pages">The gathered response bodies, in order.</param>
    /// <returns>
    /// The records as JSON text, one per item, and whether they really are records — false means the
    /// items are whole response bodies.
    /// </returns>
    public static (IReadOnlyList<string> Items, bool AreRecords) ExtractRecords(IReadOnlyList<string>? pages)
    {
        if (pages is null || pages.Count == 0)
            return (Array.Empty<string>(), false);

        var records = new List<string>();

        foreach (string page in pages)
        {
            if (!TryReadRecords(page, out JsonElement rows))
            {
                // The FIRST page decides the shape for the whole call. A later page that cannot be
                // read is a partial result, not a reason to hand back a mixture of records and raw
                // bodies that nothing downstream could tell apart.
                if (records.Count == 0)
                    return (pages, false);

                continue;
            }

            foreach (JsonElement row in rows.EnumerateArray())
                records.Add(row.GetRawText());
        }

        return records.Count > 0 ? (records, true) : (pages, false);
    }

    /// <summary>
    /// Counts the records in one response body, and the total the API says matched.
    /// </summary>
    /// <remarks>
    /// The count is what a pager strides by, so it is read back from the body rather than assumed
    /// from a requested page size — see <c>ApiRequest.SendPagedAsync</c>.
    /// </remarks>
    /// <param name="body">The raw response body.</param>
    /// <returns>Records in this body, and the total matching when the API reports one.</returns>
    public static (int Count, int? Total) CountRecords(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (0, null);

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            FirstRecord(document.RootElement, out int count, out long? total);
            return (count, total is { } value and >= int.MinValue and <= int.MaxValue ? (int)value : null);
        }
        catch (JsonException)
        {
            // Not JSON, so there are no records to count. A non-paging endpoint answering CSV is a
            // perfectly good endpoint; it simply cannot be walked, and the caller stops after one.
            return (0, null);
        }
    }

    /// <summary>
    /// Lists the field names on the first record of a response, for a node's own status line.
    /// </summary>
    /// <param name="body">The raw response body.</param>
    /// <returns>The field names, or an empty list when none can be read.</returns>
    public static IReadOnlyList<string> FieldNames(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<string>();

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement? first = FirstRecord(document.RootElement, out _, out _);
            if (first is null || first.Value.ValueKind != JsonValueKind.Object)
                return Array.Empty<string>();

            return first.Value.EnumerateObject().Select(p => p.Name).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? TrySummarizeJson(JsonElement root, int rawLength, int budget)
    {
        JsonElement? first = FirstRecord(root, out int pageCount, out long? totalCount);
        if (first is null)
            return null;

        var sb = new StringBuilder();
        sb.Append("The response held ").Append(pageCount).Append(pageCount == 1 ? " record" : " records");
        if (totalCount is { } total && total != pageCount)
            sb.Append(" of ").Append(total).Append(" matching in total");
        sb.AppendLine(".");

        if (first.Value.ValueKind == JsonValueKind.Object)
        {
            var names = first.Value.EnumerateObject().Select(p => p.Name).ToList();
            sb.Append("Fields: ").AppendLine(string.Join(", ", names));
        }

        sb.AppendLine();
        sb.AppendLine("First record:");

        string sample = JsonSerializer.Serialize(first.Value, new JsonSerializerOptions { WriteIndented = true });
        int sampleBudget = Math.Max(200, budget - sb.Length - 240);
        sb.AppendLine(Truncate(sample, sampleBudget));

        sb.AppendLine();
        sb.Append("The full response (")
          .Append(rawLength)
          .AppendLine(" characters) is on this node's Response output, one item per record, where the")
          .AppendLine("definition can read it.")
          .Append("Narrow the query, or ask for fewer fields, to see more of it here.");

        return sb.ToString();
    }

    // The rows of a response, however this particular API chose to nest them, plus how many there
    // are on this page and how many matched overall.
    // The rows of one response body, wherever this API nests them. Shares RecordKeys with the pager
    // and the summariser, so all three agree about what a record is.
    private static bool TryReadRecords(string? body, out JsonElement rows)
    {
        rows = default;

        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                rows = root.Clone();
                return true;
            }

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            foreach (string key in RecordKeys)
            {
                if (root.TryGetProperty(key, out JsonElement found) && found.ValueKind == JsonValueKind.Array)
                {
                    // Cloned because the JsonDocument is disposed on the way out of this method and
                    // the elements would otherwise point into freed memory.
                    rows = found.Clone();
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static JsonElement? FirstRecord(JsonElement root, out int pageCount, out long? totalCount)
    {
        pageCount = 0;
        totalCount = null;

        if (root.ValueKind == JsonValueKind.Array)
        {
            pageCount = root.GetArrayLength();
            return pageCount == 0 ? null : root[0];
        }

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        foreach (string key in CountKeys)
        {
            if (root.TryGetProperty(key, out JsonElement count)
                && count.ValueKind == JsonValueKind.Number
                && count.TryGetInt64(out long parsed))
            {
                totalCount = parsed;
                break;
            }
        }

        foreach (string key in RecordKeys)
        {
            if (root.TryGetProperty(key, out JsonElement rows) && rows.ValueKind == JsonValueKind.Array)
            {
                pageCount = rows.GetArrayLength();
                return pageCount == 0 ? null : rows[0];
            }
        }

        return null;
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;

        return value.Substring(0, max) + $"\n… [truncated {value.Length - max} characters]";
    }
}
