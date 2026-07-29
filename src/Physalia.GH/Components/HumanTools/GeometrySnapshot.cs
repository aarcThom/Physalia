// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.HumanTools;

namespace Physalia.GH.Components;

/// <summary>
/// Emits a <see cref="GeometrySnapshotTool"/> that arms the chat window's geometry button:
/// while it is wired into the Conversation Log's Human Tools input and a transmitter has generated
/// geometry (the script a Py Transmitter targets, or components a Component Transmitter placed),
/// the prompt box shows a geometry button. Pressing it captures a Rhino viewport snapshot of that
/// geometry — the same capture the Geometry Observation guardrail performs. By default the snapshot
/// is sent right away as its own user message carrying a built-in default message (editable from the
/// chat window's grounding panel); with "Send With Default Message" unchecked it is instead attached
/// to the prompt box like a pasted image, for the human to caption themselves. Has no inputs.
/// <para>
/// Use <see cref="ViewSnapshot"/> instead when you want the viewport as-is, with no geometry scan and
/// no camera move.
/// </para>
/// </summary>
public class GeometrySnapshot : SnapshotToolComponentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeometrySnapshot"/> class.
    /// </summary>
    public GeometrySnapshot()
        : base("Geometry Snapshot", "GeoSnap", "Adds a geometry button to the chat window that captures a viewport snapshot of the transmitter-generated geometry — sent as its own message, or attached to the prompt box for you to caption.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D5B8F2A6-7E31-4C94-A0D8-3F6E1B9C5A27");

    /// <inheritdoc/>
    protected override HumanTool Tool => new GeometrySnapshotTool(GeometrySnapshotTool.DefaultMessage, SendWithMessage);
}
