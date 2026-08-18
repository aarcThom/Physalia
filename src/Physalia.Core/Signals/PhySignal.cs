// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Physalia.Core.Common;
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
    /// Gets the components this event ultimately came FROM, when that differs from the component
    /// that minted it. An aggregator (Merge Signal's join, the Feedback Collector's batch) and an
    /// escalating pass-through (Stall Guard) re-mint under their own identity, which would otherwise
    /// erase the producer of the text; they carry the trail forward instead. Empty on an original
    /// mint, where <see cref="SourceId"/>/<see cref="SourceName"/> ARE the origin — read
    /// <see cref="OriginTrail"/> rather than this, and never branch on it.
    ///
    /// <para>Provenance, not a carrier: it sits with <see cref="SourceId"/>, <see cref="SourceName"/>
    /// and <see cref="Timestamp"/> as trace metadata about the event, and holds no data the pipeline
    /// acts on. The carrier discipline above is untouched — Payload, ContentBlocks and Instructions
    /// remain the only things a signal carries.</para>
    /// </summary>
    public IReadOnlyList<ComponentOrigin> Origins { get; init; } = Array.Empty<ComponentOrigin>();

    /// <summary>
    /// Gets the origin trail to attribute this event to: <see cref="Origins"/> when it was carried
    /// forward through an aggregator, else the emitting component itself. Always at least one entry,
    /// so callers never special-case an original mint.
    /// </summary>
    public IReadOnlyList<ComponentOrigin> OriginTrail =>
        Origins.Count > 0 ? Origins : new[] { new ComponentOrigin(SourceId, SourceName) };

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
    /// <param name="origins">
    /// Optional trail of the components this event ultimately came from, for a signal that re-mints
    /// someone else's event (an aggregator, an escalating pass-through); null on an original mint,
    /// where the emitting component is the origin.
    /// </param>
    /// <returns>A freshly sequenced signal.</returns>
    public static PhySignal Mint(SignalOutcome outcome, string? payload, Guid sourceId, string sourceName, IReadOnlyList<MessageContent>? contentBlocks = null, Instructions? instructions = null, IReadOnlyList<ComponentOrigin>? origins = null) =>
        new(SignalSequencer.Next(), outcome, payload ?? string.Empty, sourceId, sourceName, DateTime.UtcNow)
        {
            ContentBlocks = contentBlocks ?? Array.Empty<MessageContent>(),
            Instructions = instructions,
            Origins = origins ?? Array.Empty<ComponentOrigin>(),
        };
}
