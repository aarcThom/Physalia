// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Api;

/// <summary>
/// What one paged read gathered: the response bodies, in fetch order, and how the walk ended.
/// </summary>
/// <remarks>
/// <b>Pages are kept whole rather than merged.</b> Merging would mean deciding what an API's envelope
/// means — which key holds the rows, what to do with two disagreeing <c>total_count</c> values — and
/// getting that wrong silently reshapes the user's data. Handing over the bodies leaves the parsing
/// to the Python component that was going to parse them anyway, and it iterates pages in one line.
/// </remarks>
/// <param name="Pages">Each response body, in the order it was fetched. Never null; may be empty.</param>
/// <param name="RecordCount">How many records were gathered across all pages.</param>
/// <param name="MatchedCount">
/// How many records matched in total, when the API said so — which is not the same as
/// <paramref name="RecordCount"/> and is the number that tells a caller whether it has everything.
/// Null when the API reports no total.
/// </param>
/// <param name="StoppedBecause">
/// Why the walk ended early, in words fit to hand to the model, or null when it ended because there
/// was nothing left to fetch. A partial read that does not say so is the failure worth avoiding here.
/// </param>
public sealed record ApiPagedResponse(
    IReadOnlyList<string> Pages,
    int RecordCount,
    int? MatchedCount,
    string? StoppedBecause)
{
    /// <summary>
    /// Gets a value indicating whether records are known to have been left behind.
    /// </summary>
    public bool IsPartial =>
        this.StoppedBecause is not null
        || (this.MatchedCount is { } matched && this.RecordCount < matched);
}
