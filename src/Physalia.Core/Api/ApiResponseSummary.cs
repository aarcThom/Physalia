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
          .AppendLine(" characters) is on this node's Response output, where the definition can read it.")
          .Append("Narrow the query, or ask for fewer fields, to see more of it here.");

        return sb.ToString();
    }

    // The rows of a response, however this particular API chose to nest them, plus how many there
    // are on this page and how many matched overall.
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
