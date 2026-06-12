// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Physalia.Core.Signals;

/// <summary>
/// Whether the run that minted a signal succeeded or failed.
/// </summary>
public enum SignalOutcome
{
    /// <summary>The emitting component's run completed without error.</summary>
    Success,

    /// <summary>The emitting component's run completed with an error; the payload carries feedback.</summary>
    Failure,
}
