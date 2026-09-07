// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System.Runtime.CompilerServices;
using Grasshopper.Kernel;
using Physalia.Core.Budget;
using Physalia.Core.Common;

namespace Physalia.GH.Components;

/// <summary>
/// What each pipeline has spent this session, so a Budget Guard can refuse a round that would go past
/// its cap.
///
/// <para><b>Keyed on the LOCAL document</b> — the harness sub-document a pipeline lives in, or the
/// canvas for a pipeline standing on it. That is how the two halves of this find each other without a
/// wire between them: the LLM Call records, the Budget Guard reads, and neither needs to know the
/// other exists. The PDF registry pairs its two halves the same way and for the same reason.</para>
///
/// <para><b>Session-only, like everything else in the lifecycle.</b> A budget is a bound on what this
/// sitting of work may spend, not an accounting record — that is what a run log is for. Persisting it
/// would also mean a shared pipeline arriving with someone else's spending already against it, which
/// is the same argument that keeps a trigger from opening armed.</para>
///
/// <para><b>A call with no usage reported still counts as a call.</b> Not every provider reports token
/// usage — a CLI provider driving a subscription has no per-call figure to give — so a token cap alone
/// cannot bound those at all. Counting the call regardless is what makes Max Calls the honest cap for
/// them, and it is why that axis exists next to tokens rather than instead of it.</para>
/// </summary>
internal static class SpendLedger
{
    // Weak on the document, so a closed file's tally is collected with it. A mutable box because the
    // table stores references and Spend is immutable.
    private static readonly ConditionalWeakTable<GH_Document, Box> Tallies = new();

    private static readonly object Gate = new();

    /// <summary>
    /// Records one inference call against a document's tally.
    /// </summary>
    /// <param name="document">The document the LLM Call lives on.</param>
    /// <param name="usage">The usage the provider reported, or null when it reported none.</param>
    internal static void Record(GH_Document? document, LlmUsage? usage)
    {
        if (document is null)
        {
            return;
        }

        // Cached tokens are not included in InputTokens, so they are added back — the same correction
        // the LLM Call's own usage note makes. Reporting InputTokens alone makes a cache hit look like
        // the prompt shrank, and a budget built on that would drift further out the longer a
        // conversation ran.
        long tokens = usage is null
            ? 0
            : usage.InputTokens + usage.OutputTokens + usage.CacheWriteTokens + usage.CacheReadTokens;

        lock (Gate)
        {
            Box box = Tallies.GetValue(document, _ => new Box());
            box.Spend = box.Spend.Plus(tokens);
        }
    }

    /// <summary>
    /// What a document has spent so far.
    /// </summary>
    /// <param name="document">The document to read.</param>
    /// <returns>The tally, or nothing spent.</returns>
    internal static Spend Read(GH_Document? document)
    {
        if (document is null)
        {
            return Spend.Nothing;
        }

        lock (Gate)
        {
            return Tallies.TryGetValue(document, out Box? box) ? box.Spend : Spend.Nothing;
        }
    }

    /// <summary>
    /// Puts a document's tally back to nothing.
    /// </summary>
    /// <param name="document">The document to reset.</param>
    internal static void Reset(GH_Document? document)
    {
        if (document is null)
        {
            return;
        }

        lock (Gate)
        {
            Tallies.GetValue(document, _ => new Box()).Spend = Spend.Nothing;
        }
    }

    private sealed class Box
    {
        internal Spend Spend { get; set; } = Spend.Nothing;
    }
}
