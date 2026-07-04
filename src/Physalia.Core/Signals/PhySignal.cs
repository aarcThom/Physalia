// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.ConvoInstruct;

namespace Physalia.Core.Signals;

/// <summary>
/// An immutable, sequence-numbered event. Signals replace momentary boolean trigger
/// pulses: they latch on wires and downstream components consume each one exactly once,
/// keyed by <see cref="Sequence"/>. Ordering between events is defined by the sequence,
/// never by solve timing, so coalesced, delayed, or replayed solves cannot reorder,
/// duplicate, or drop events.
///
/// <para><b>Carrier discipline (do not erode).</b> The signal carries exactly three things:
/// <see cref="Payload"/> (the text trace / feedback string), <see cref="ContentBlocks"/> (a
/// richer-than-text user turn, e.g. inline images — the Chat→Conversation Log hop), and
/// <see cref="Instructions"/> (the full inference context — the Conversation Log→LLM Call hop). These are
/// the inter-component <em>events</em> the pipeline is built on. Do NOT add further typed carrier
/// fields: arbitrary data belongs on typed wires/inputs, not bolted onto the signal. Every new field
/// here makes the signal a god-object and dilutes "the signal is the event".</para>
/// </summary>
/// <param name="Sequence">Process-wide monotonic identity; higher = happened later.</param>
/// <param name="Outcome">Whether the emitting run succeeded or failed.</param>
/// <param name="Payload">The event's payload: the data string on success, the feedback string on failure.</param>
/// <param name="SourceId">Instance GUID of the emitting component.</param>
/// <param name="SourceName">Display name of the emitting component, for tracing.</param>
/// <param name="Timestamp">UTC mint time, for tracing.</param>
public sealed record PhySignal(
    long Sequence,
    SignalOutcome Outcome,
    string Payload,
    Guid SourceId,
    string SourceName,
    DateTime Timestamp)
{
    /// <summary>
    /// Gets the resolved content blocks for the event, when it carries richer-than-text data
    /// (e.g. a Chat user turn with inline images). Empty for the common text-only case, in
    /// which <see cref="Payload"/> is the sole carrier. A deliberate multimodal extension to the
    /// payload-only contract: the only wire between pipeline components is the signal, so an
    /// assembled multimodal turn must ride on it.
    /// </summary>
    public IReadOnlyList<MessageContent> ContentBlocks { get; init; } = Array.Empty<MessageContent>();

    /// <summary>
    /// Gets the full inference context (system prompt + conversation) carried by the event, when one
    /// applies — the Conversation Log→LLM Call hop, where the trigger signal <em>is</em> the data. Null for
    /// every other signal (feedback, tool results, manual triggers). A compaction component consumes a
    /// signal carrying these Instructions and re-emits one carrying the compacted Instructions. The
    /// conversation is reachable via <c>Instructions.Conversation</c>; the goo casts a signal straight
    /// to Instructions/Conversation so a typed input can consume it without manual deconstruction.
    /// </summary>
    public Instructions? Instructions { get; init; }

    /// <summary>
    /// Mints a new signal with the next global sequence number. This is the only way a
    /// sequence is assigned — callers can never reuse or fabricate sequence numbers.
    /// </summary>
    /// <param name="outcome">Whether the emitting run succeeded or failed.</param>
    /// <param name="payload">The event payload; null is normalised to empty.</param>
    /// <param name="sourceId">Instance GUID of the emitting component.</param>
    /// <param name="sourceName">Display name of the emitting component.</param>
    /// <param name="contentBlocks">Optional resolved content blocks; null is normalised to empty.</param>
    /// <param name="instructions">Optional full inference context carried by the event (Conversation Log→LLM Call); null otherwise.</param>
    /// <returns>A freshly sequenced signal.</returns>
    public static PhySignal Mint(SignalOutcome outcome, string? payload, Guid sourceId, string sourceName, IReadOnlyList<MessageContent>? contentBlocks = null, Instructions? instructions = null) =>
        new(SignalSequencer.Next(), outcome, payload ?? string.Empty, sourceId, sourceName, DateTime.UtcNow)
        {
            ContentBlocks = contentBlocks ?? Array.Empty<MessageContent>(),
            Instructions = instructions,
        };
}
