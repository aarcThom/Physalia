// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.HumanTools;

namespace Physalia.GH.Components;

/// <summary>
/// Emits a <see cref="SignalTraceTool"/> that puts a signal-trace button at the top of the chat
/// window: pressing it opens the Physalia signal-trace window — every signal that reached a wire
/// this session, with its payload, carried content, and consumption timeline. Wire it into the
/// Conversation Log's Human Tools input; without it the trace window has no opener. Has no inputs.
/// <para>
/// The trace itself is process-wide and session-only (it records every Physalia signal in the
/// Rhino session, not just this conversation's), so wiring several of these adds no state — the
/// button is just a door onto the one log.
/// </para>
/// </summary>
public class SignalTrace : HumanToolComponentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SignalTrace"/> class.
    /// </summary>
    public SignalTrace()
        : base("Signal Trace", "Trace", "Adds a button to the chat window that opens the Physalia signal-trace window.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("503C8B2E-AF01-4870-A8FA-EE3731844565");

    /// <inheritdoc/>
    protected override HumanTool Tool => new SignalTraceTool();
}
