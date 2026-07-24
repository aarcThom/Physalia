// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.HumanTools;

namespace Physalia.GH.Components;

/// <summary>
/// Emits a <see cref="GeometrySnapshotTool"/> that arms the chat window's geometry button:
/// while it is wired into the Conversation Log's Human Tools input and a transmitter has generated
/// geometry (the script a Py Transmitter targets, or components a Component Transmitter placed),
/// the prompt box shows a geometry button. Pressing it sends a Rhino viewport snapshot of that
/// geometry — the same capture the Geometry Observation guardrail performs — as its own user
/// message; a snapshot is never attached to a typed prompt automatically. The accompanying message
/// has a built-in default and is editable from the chat window's grounding panel. Has no inputs.
/// </summary>
public class GeometrySnapshot : HumanToolComponentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeometrySnapshot"/> class.
    /// </summary>
    public GeometrySnapshot()
        : base("Geometry Snapshot", "GeoSnap", "Adds a geometry button to the chat window that sends a viewport snapshot of the transmitter-generated geometry as its own message.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("D5B8F2A6-7E31-4C94-A0D8-3F6E1B9C5A27");

    /// <inheritdoc/>
    protected override HumanTool Tool => new GeometrySnapshotTool(GeometrySnapshotTool.DefaultMessage);
}
