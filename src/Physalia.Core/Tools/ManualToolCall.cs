// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Tools;

/// <summary>
/// The convention that tells a tool call the PIPELINE made from one the MODEL made.
/// </summary>
/// <remarks>
/// <para>A tool node is normally driven by a Router dispatching what the model asked for. It can
/// also be driven by hand — a Construct Tool Call node, or a script composing a query — which costs
/// nothing to allow, because the tool base already reads its calls out of the signal's content
/// blocks and does not care who put them there.</para>
/// <para><b>What it does cost is the result.</b> A <see cref="ToolResultContent"/> must appear in a
/// user turn echoing the id of a <see cref="ToolCallContent"/> the assistant actually emitted;
/// Anthropic rejects the request outright when it does not, which is the same failure the compaction
/// window's tool pairing exists to prevent. A hand-made call has no such assistant turn, so its
/// result must never reach the conversation — the data goes on the node's own output instead, and
/// the Result signal is not emitted at all.</para>
/// <para>Marking the call itself is what makes that decidable. The alternative — trusting the user
/// not to wire the Result output while also driving the node by hand — is not a design, because the
/// model path REQUIRES that wire. Correctness by identity, the way the rest of the signal layer
/// works.</para>
/// </remarks>
public static class ManualToolCall
{
    /// <summary>
    /// The prefix marking a call id as pipeline-made rather than model-made.
    /// </summary>
    /// <remarks>
    /// A colon cannot appear in a provider-issued id — Anthropic uses <c>toolu_…</c>, OpenAI
    /// <c>call_…</c>, Gemini a bare name — so nothing the model emits can be mistaken for one of
    /// these, in either direction.
    /// </remarks>
    public const string IdPrefix = "manual:";

    /// <summary>
    /// Mints an id for a hand-made call.
    /// </summary>
    /// <returns>A unique id carrying the manual marker.</returns>
    public static string NewId() => IdPrefix + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Reports whether a call id was minted by the pipeline rather than issued by a model.
    /// </summary>
    /// <param name="id">The call id.</param>
    /// <returns>True when this is a hand-made call.</returns>
    public static bool IsManual(string? id) =>
        id is not null && id.StartsWith(IdPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Reports whether every call in a batch was made by hand.
    /// </summary>
    /// <remarks>
    /// All-or-nothing on purpose. A batch is one dispatched signal, and a signal comes from either
    /// the Router or a manual mint — never both — so a MIXED batch means something upstream is
    /// wrong. Treating a mixed batch as model-driven is the safe reading of that: the model's calls
    /// get answered, which is what leaving them unanswered would otherwise cost (a round that never
    /// completes), and the stray manual result is merely noise in the log.
    /// </remarks>
    /// <param name="calls">The calls in one dispatched batch.</param>
    /// <returns>True when the batch is non-empty and every call is manual.</returns>
    public static bool IsManualBatch(IReadOnlyList<ToolCallContent> calls)
    {
        if (calls is null || calls.Count == 0)
            return false;

        foreach (ToolCallContent call in calls)
        {
            if (!IsManual(call.Id))
                return false;
        }

        return true;
    }
}
