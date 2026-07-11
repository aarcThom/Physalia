// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using Grasshopper.Kernel;

namespace Physalia.GH.Diagnostics;

/// <summary>
/// One runtime error or warning observed on a signal-lifecycle component, with the wall-clock
/// window it was actually displayed for. Presence is sampled at every document solution end, so
/// <see cref="StartUtc"/> is the end of the solution where the message first appeared and
/// <see cref="EndUtc"/> the end of the solution where it was gone — a message that flashes
/// during a burst of scheduled solves therefore records a short duration (safely ignorable),
/// while one that persists records how long it genuinely stood. Immutable —
/// <see cref="RuntimeMessageTrace"/> replaces the record wholesale when the message clears.
/// </summary>
/// <param name="Id">Monotonic identity within the trace, used to close the record when the message clears.</param>
/// <param name="ComponentId">Instance GUID of the component displaying the message.</param>
/// <param name="ComponentName">Display name of the component displaying the message.</param>
/// <param name="Level">The message level (Error or Warning; Remarks are not traced).</param>
/// <param name="Text">The runtime message text.</param>
/// <param name="StartUtc">UTC time the message was first observed.</param>
public sealed record MessageTraceEntry(
    long Id,
    Guid ComponentId,
    string ComponentName,
    GH_RuntimeMessageLevel Level,
    string Text,
    DateTime StartUtc)
{
    /// <summary>
    /// Gets the UTC time the message was observed gone, or null while it is still displayed
    /// (or was still displayed when recording stopped).
    /// </summary>
    public DateTime? EndUtc { get; init; }

    /// <summary>
    /// Gets how long the message was displayed: the observed window when closed, otherwise the
    /// time from first observation to now (still showing).
    /// </summary>
    /// <returns>The display duration.</returns>
    public TimeSpan DisplayedFor => (EndUtc ?? DateTime.UtcNow) - StartUtc;
}
