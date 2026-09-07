// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Physalia.Core.Recording;

/// <summary>
/// One inference call, as it goes into the run log.
/// </summary>
/// <param name="When">When the call finished, UTC.</param>
/// <param name="Harness">The harness that made it, by name.</param>
/// <param name="Model">The model id asked.</param>
/// <param name="InputTokens">Prompt tokens, cached ones included.</param>
/// <param name="OutputTokens">Completion tokens.</param>
/// <param name="DurationMs">How long the call took, wall clock.</param>
/// <param name="Succeeded">Whether a reply came back.</param>
/// <param name="StopReason">Why the model stopped, when it said. Null otherwise.</param>
/// <param name="Error">What went wrong, for a failed call. Null otherwise.</param>
public sealed record RunRecord(
    DateTime When,
    string Harness,
    string Model,
    int InputTokens,
    int OutputTokens,
    long DurationMs,
    bool Succeeded,
    string? StopReason,
    string? Error);

/// <summary>
/// A pipeline's record of what it actually did — one line per inference call, appended to a file in
/// the project folder.
///
/// <para><b>Why this is not the budget.</b> A Budget Guard bounds what a session MAY spend and
/// forgets everything when Rhino closes, because a bound on this sitting of work is all it is. This
/// is the other question: what did this pipeline cost, over the weeks it ran, and what was it doing
/// when it cost that. Neither answers the other, and only one of them is worth keeping on disk.</para>
///
/// <para><b>JSONL, not JSON.</b> A log is a stream: every line stands alone, an append is a write with
/// nothing to re-read first, and a truncated last line costs one record rather than the file. That is
/// the opposite of <c>downloads.json</c>, which is a LEDGER — a set that has to be read whole to
/// answer "is this file accounted for". The formats differ because the questions do.</para>
///
/// <para><b>Deliberately not per-round or per-turn.</b> A round is a fuzzy boundary — a tool round is
/// several calls, a feedback loop is several rounds, and a user would not agree with any line we drew.
/// An inference call is unambiguous, is the thing that costs money, and is the level at which every
/// fact here is actually known.</para>
/// </summary>
public static class RunLog
{
    /// <summary>The log file's name inside the project folder.</summary>
    public const string FileName = "runs.jsonl";

    /// <summary>
    /// Formats one record as a single line of JSON, newline included.
    /// </summary>
    /// <param name="record">The call to record.</param>
    /// <returns>The line to append.</returns>
    public static string Line(RunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var fields = new Dictionary<string, object?>
        {
            // Sortable and unambiguous, which a log read by machine and by eye both need.
            ["when"] = record.When.ToString("o", CultureInfo.InvariantCulture),
            ["harness"] = record.Harness,
            ["model"] = record.Model,
            ["inputTokens"] = record.InputTokens,
            ["outputTokens"] = record.OutputTokens,
            ["ms"] = record.DurationMs,
            ["ok"] = record.Succeeded,
        };

        if (!string.IsNullOrWhiteSpace(record.StopReason))
        {
            fields["stop"] = record.StopReason;
        }

        if (!string.IsNullOrWhiteSpace(record.Error))
        {
            fields["error"] = record.Error;
        }

        // Never indented: one record is one line, which is the whole point of the format.
        return JsonSerializer.Serialize(fields) + "\n";
    }

    /// <summary>
    /// Reads a log back, skipping anything unreadable.
    ///
    /// <para>Skipping rather than failing is the right call for a log: a line half-written when Rhino
    /// was killed must not cost the months of records above it.</para>
    /// </summary>
    /// <param name="text">The file's contents.</param>
    /// <returns>The records that parsed, in file order.</returns>
    public static IReadOnlyList<RunRecord> Parse(string? text)
    {
        var records = new List<RunRecord>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return records;
        }

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            RunRecord? record = TryParse(trimmed);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records;
    }

    /// <summary>
    /// Sums a log into the one line someone actually wants: how many calls, how many tokens, how
    /// long, and how many failed.
    /// </summary>
    /// <param name="records">The records to sum.</param>
    /// <returns>A single descriptive line.</returns>
    public static string Summarise(IReadOnlyList<RunRecord>? records)
    {
        if (records is null || records.Count == 0)
        {
            return "No calls recorded.";
        }

        long input = 0;
        long output = 0;
        long ms = 0;
        int failed = 0;

        foreach (RunRecord record in records)
        {
            input += record.InputTokens;
            output += record.OutputTokens;
            ms += record.DurationMs;

            if (!record.Succeeded)
            {
                failed++;
            }
        }

        string calls = records.Count == 1 ? "1 call" : $"{records.Count.ToString(CultureInfo.InvariantCulture)} calls";
        string tokens = $"{Budget.SpendPolicy.Tokens(input)} in, {Budget.SpendPolicy.Tokens(output)} out";
        string time = ms >= 60_000
            ? $"{(ms / 60_000.0).ToString("0.#", CultureInfo.InvariantCulture)} min"
            : $"{(ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)} s";

        return failed > 0
            ? $"{calls} ({failed} failed) · {tokens} · {time}"
            : $"{calls} · {tokens} · {time}";
    }

    private static RunRecord? TryParse(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new RunRecord(
                root.TryGetProperty("when", out JsonElement when)
                    && DateTime.TryParse(when.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed)
                        ? parsed
                        : DateTime.MinValue,
                Str(root, "harness"),
                Str(root, "model"),
                Int(root, "inputTokens"),
                Int(root, "outputTokens"),
                root.TryGetProperty("ms", out JsonElement ms) && ms.TryGetInt64(out long duration) ? duration : 0,
                !root.TryGetProperty("ok", out JsonElement ok) || ok.ValueKind != JsonValueKind.False,
                root.TryGetProperty("stop", out JsonElement stop) ? stop.GetString() : null,
                root.TryGetProperty("error", out JsonElement error) ? error.GetString() : null);
        }
        catch (JsonException)
        {
            // A line half-written when Rhino was killed. Skipped; see the method remarks.
            return null;
        }
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : 0;
}
