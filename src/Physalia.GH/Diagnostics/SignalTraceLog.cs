// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Physalia.Core.ConvoInstruct;
using Physalia.Core.Signals;

namespace Physalia.GH.Diagnostics;

/// <summary>
/// Process-wide, session-only trace of every signal that reached a wire, fed by three taps:
/// <c>StatefulComponentBase.EmitSignal</c> (every wired emission — deduped by sequence, since a
/// latched signal re-emits on every solve), <c>StatefulComponentBase.MarkConsumed</c> (every
/// consume-once advance, with the consumer's identity), and <c>FeedbackCollector.Inject</c>
/// (the wireless hop). Capture is inherently gated on Physalia presence: the taps live inside
/// Physalia component code, so nothing runs when no Physalia component is on the canvas.
///
/// <para>Entries are lightweight snapshots (see <see cref="SignalTraceEntry"/>) capped at
/// <see cref="Capacity"/> with oldest-first eviction, so the trace never pins images or
/// conversation history and its memory is bounded. Nothing here serializes — like the signals
/// themselves, the trace is a session artifact. The UI polls <see cref="Version"/> and calls
/// <see cref="Snapshot"/> on change, so no events cross from solve threads to the UI.</para>
/// </summary>
internal static class SignalTraceLog
{
    /// <summary>Maximum number of traced signals retained; the oldest entry is evicted past this.</summary>
    internal const int Capacity = 500;

    /// <summary>Payload characters retained per entry; longer payloads are truncated and flagged.</summary>
    internal const int MaxPayloadChars = 64 * 1024;

    private static readonly object Gate = new();
    private static readonly Dictionary<long, SignalTraceEntry> Entries = new();
    private static readonly List<long> Order = new();

    private static int _version;

    /// <summary>
    /// Gets the mutation counter, bumped on every recorded emission, consumption, and clear.
    /// The trace window polls this to decide when to re-read <see cref="Snapshot"/>.
    /// </summary>
    internal static int Version
    {
        get
        {
            lock (Gate)
            {
                return _version;
            }
        }
    }

    /// <summary>
    /// Records a signal reaching a wire. Called on every solve for a latched signal, so the
    /// sequence number dedupes: only the first emission creates an entry.
    /// </summary>
    /// <param name="signal">The emitted signal.</param>
    internal static void RecordEmission(PhySignal signal)
    {
        lock (Gate)
        {
            if (Entries.ContainsKey(signal.Sequence))
            {
                return;
            }

            Add(BuildEntry(signal));
            _version++;
        }
    }

    /// <summary>
    /// Records a component consuming a signal on one of its inputs, joined to the traced entry
    /// by sequence number. A consumption for an unknown sequence (possible after a mid-session
    /// Clear) creates a stub entry so the record is never dropped silently.
    /// </summary>
    /// <param name="sequence">The consumed signal's sequence number.</param>
    /// <param name="consumerId">Instance GUID of the consuming component.</param>
    /// <param name="consumerName">Display name of the consuming component.</param>
    /// <param name="inputName">Name of the input the signal arrived on.</param>
    /// <param name="timeUtc">UTC time of the consumption.</param>
    internal static void RecordConsumption(long sequence, Guid consumerId, string consumerName, string inputName, DateTime timeUtc)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue(sequence, out SignalTraceEntry? entry))
            {
                entry = new SignalTraceEntry(
                    sequence,
                    SignalOutcome.Success,
                    Guid.Empty,
                    "(untraced)",
                    timeUtc,
                    string.Empty,
                    PayloadTruncated: false,
                    Array.Empty<ContentBlockSummary>(),
                    Instructions: null,
                    IsStub: true);
                Add(entry);
            }

            var consumptions = new List<ConsumptionRecord>(entry.Consumptions)
            {
                new ConsumptionRecord(consumerId, consumerName, inputName, timeUtc),
            };
            Entries[sequence] = entry with { Consumptions = consumptions };
            _version++;
        }
    }

    /// <summary>
    /// Records a wireless Feedback → Feedback Collector injection: ensures the signal is traced
    /// (it normally already is, from its emission) and appends a consumption naming the collector
    /// with the input name "(wireless)". The collector's fresh batch signal is traced separately
    /// by its own emission.
    /// </summary>
    /// <param name="signal">The injected signal.</param>
    /// <param name="collectorId">Instance GUID of the receiving Feedback Collector.</param>
    /// <param name="collectorName">Display name of the receiving Feedback Collector.</param>
    internal static void RecordWirelessInjection(PhySignal signal, Guid collectorId, string collectorName)
    {
        RecordEmission(signal);
        RecordConsumption(signal.Sequence, collectorId, collectorName, "(wireless)", DateTime.UtcNow);
    }

    /// <summary>
    /// Takes an immutable snapshot of every traced entry in arrival order (oldest first).
    /// Entries are immutable records, so the returned references are safe to read on any thread.
    /// </summary>
    /// <returns>The traced entries, oldest first.</returns>
    internal static IReadOnlyList<SignalTraceEntry> Snapshot()
    {
        lock (Gate)
        {
            return Order.Select(seq => Entries[seq]).ToList();
        }
    }

    /// <summary>
    /// Drops every traced entry. Consumptions arriving afterwards for already-dropped signals
    /// create stub entries.
    /// </summary>
    internal static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
            Order.Clear();
            _version++;
        }
    }

    // Inserts an entry and evicts the oldest past capacity. Caller holds the lock.
    private static void Add(SignalTraceEntry entry)
    {
        Entries[entry.Sequence] = entry;
        Order.Add(entry.Sequence);

        while (Order.Count > Capacity)
        {
            Entries.Remove(Order[0]);
            Order.RemoveAt(0);
        }
    }

    // Reduces a live signal to its lightweight trace entry (no references retained).
    private static SignalTraceEntry BuildEntry(PhySignal signal)
    {
        string payload = signal.Payload ?? string.Empty;
        bool truncated = payload.Length > MaxPayloadChars;
        if (truncated)
        {
            payload = payload[..MaxPayloadChars];
        }

        var blocks = new List<ContentBlockSummary>(signal.ContentBlocks.Count);
        foreach (MessageContent block in signal.ContentBlocks)
        {
            blocks.Add(Summarize(block));
        }

        InstructionsSummary? instructions = signal.Instructions is { } instr
            ? new InstructionsSummary(instr.SystemPrompt.Text.Length, instr.Conversation.Count, instr.Tools.Count)
            : null;

        return new SignalTraceEntry(
            signal.Sequence,
            signal.Outcome,
            signal.SourceId,
            signal.SourceName,
            signal.Timestamp,
            payload,
            truncated,
            blocks,
            instructions);
    }

    private static ContentBlockSummary Summarize(MessageContent block) => block switch
    {
        TextContent text => new ContentBlockSummary("text", $"{text.Text?.Length ?? 0} chars"),
        ImageContent image => new ContentBlockSummary("image", SummarizeImage(image.Source)),
        ToolCallContent call => new ContentBlockSummary("tool call", $"{call.Name} (id {call.Id}, input {call.InputJson?.Length ?? 0} chars)"),
        ToolResultContent result => new ContentBlockSummary("tool result", $"id {result.ToolCallId}{(result.IsError ? ", ERROR" : string.Empty)}, {result.Content?.Length ?? 0} chars"),
        _ => new ContentBlockSummary(block.GetType().Name, string.Empty),
    };

    private static string SummarizeImage(ImageSource source) => source switch
    {
        InlineImage inline => $"{inline.MimeType}, {FormatBytes(inline.Data?.Length ?? 0)}",
        UrlImage url => $"url: {url.Url}",
        ManagedImage managed => $"file handle: {managed.FileHandle}",
        _ => source.GetType().Name,
    };

    private static string FormatBytes(int bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };
}
