// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using Physalia.Core.Signals;

namespace Physalia.GH.Diagnostics;

/// <summary>
/// One consumption of a traced signal: which component consumed it, on which input, and when.
/// A signal is consumed at most once per receiving input (consume-once semantics), so the list
/// of these records on an entry is the signal's complete fan-out.
/// </summary>
/// <param name="ConsumerId">Instance GUID of the consuming component.</param>
/// <param name="ConsumerName">Display name of the consuming component.</param>
/// <param name="InputName">Name of the input the signal arrived on, or "(wireless)" for a Feedback Collector injection.</param>
/// <param name="TimeUtc">UTC time the consumption was recorded.</param>
public sealed record ConsumptionRecord(Guid ConsumerId, string ConsumerName, string InputName, DateTime TimeUtc);

/// <summary>
/// Lightweight description of one content block carried by a traced signal. The trace never
/// retains the block itself — inline images in particular can be megabytes — only this summary.
/// </summary>
/// <param name="Kind">Human label for the block type: "text", "image", "tool call", "tool result".</param>
/// <param name="Detail">One-line detail, e.g. "image/png, 213 KB" or "create_rhino_geometry (input 412 chars)".</param>
public sealed record ContentBlockSummary(string Kind, string Detail);

/// <summary>
/// Lightweight description of the Instructions carried by a traced signal (the Conversation
/// Log → LLM Call hop). The trace never retains the Instructions reference — it holds a full
/// conversation — only these counts.
/// </summary>
/// <param name="SystemPromptChars">Character count of the system prompt.</param>
/// <param name="TurnCount">Number of turns in the conversation.</param>
/// <param name="ToolCount">Number of tool definitions advertised on the Instructions.</param>
public sealed record InstructionsSummary(int SystemPromptChars, int TurnCount, int ToolCount);

/// <summary>
/// One traced signal: a lightweight snapshot taken at first emission, plus every consumption
/// recorded since. Immutable — <see cref="SignalTraceLog"/> replaces the entry wholesale when a
/// consumption is appended (copy-on-write under its lock), so snapshots handed to the UI thread
/// are always safe to read. The original <see cref="PhySignal"/> is never retained: content
/// blocks and Instructions are reduced to summaries so the trace pins no image bytes or
/// conversation history.
/// </summary>
/// <param name="Sequence">The signal's global sequence number (its identity).</param>
/// <param name="Outcome">The outcome stamped on the signal.</param>
/// <param name="SourceId">Instance GUID of the minting component.</param>
/// <param name="SourceName">Display name of the minting component.</param>
/// <param name="TimestampUtc">UTC mint time carried by the signal.</param>
/// <param name="Payload">The payload text, truncated at the trace cap.</param>
/// <param name="PayloadTruncated">Whether <paramref name="Payload"/> was truncated.</param>
/// <param name="Blocks">Summaries of the content blocks the signal carried.</param>
/// <param name="Instructions">Summary of the Instructions the signal carried, or null.</param>
/// <param name="IsStub">
/// True for a placeholder created when a consumption arrived for a sequence the trace has no
/// emission for (possible after a mid-session Clear).
/// </param>
public sealed record SignalTraceEntry(
    long Sequence,
    SignalOutcome Outcome,
    Guid SourceId,
    string SourceName,
    DateTime TimestampUtc,
    string Payload,
    bool PayloadTruncated,
    IReadOnlyList<ContentBlockSummary> Blocks,
    InstructionsSummary? Instructions,
    bool IsStub = false)
{
    /// <summary>
    /// Gets the consumptions recorded for this signal, oldest first. Replaced wholesale (never
    /// mutated in place) when <see cref="SignalTraceLog"/> appends a record.
    /// </summary>
    public IReadOnlyList<ConsumptionRecord> Consumptions { get; init; } = Array.Empty<ConsumptionRecord>();
}
