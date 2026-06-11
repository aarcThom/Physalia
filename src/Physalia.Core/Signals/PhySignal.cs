// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Signals;

/// <summary>
/// An immutable, sequence-numbered event. Signals replace momentary boolean trigger
/// pulses: they latch on wires and downstream components consume each one exactly once,
/// keyed by <see cref="Sequence"/>. Ordering between events is defined by the sequence,
/// never by solve timing, so coalesced, delayed, or replayed solves cannot reorder,
/// duplicate, or drop events.
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
    /// Mints a new signal with the next global sequence number. This is the only way a
    /// sequence is assigned — callers can never reuse or fabricate sequence numbers.
    /// </summary>
    /// <param name="outcome">Whether the emitting run succeeded or failed.</param>
    /// <param name="payload">The event payload; null is normalised to empty.</param>
    /// <param name="sourceId">Instance GUID of the emitting component.</param>
    /// <param name="sourceName">Display name of the emitting component.</param>
    /// <returns>A freshly sequenced signal.</returns>
    public static PhySignal Mint(SignalOutcome outcome, string? payload, Guid sourceId, string sourceName) =>
        new(SignalSequencer.Next(), outcome, payload ?? string.Empty, sourceId, sourceName, DateTime.UtcNow);
}
