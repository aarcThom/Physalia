// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Signals;

/// <summary>
/// Process-wide monotonic sequence source for <see cref="PhySignal"/>. Sequence numbers
/// define the global causal order of events: a signal minted as a consequence of another
/// always carries a higher sequence. Zero is reserved to mean "never / unset".
/// </summary>
public static class SignalSequencer
{
    private static long _last;

    /// <summary>
    /// Returns the next sequence number. Thread-safe; first value is 1.
    /// </summary>
    /// <returns>A monotonically increasing, process-unique sequence number.</returns>
    public static long Next() => Interlocked.Increment(ref _last);
}
