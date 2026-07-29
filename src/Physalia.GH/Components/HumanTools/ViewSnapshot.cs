// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.HumanTools;

namespace Physalia.GH.Components;

/// <summary>
/// Emits a <see cref="ViewSnapshotTool"/> that adds a view button to the chat window: pressing it
/// captures the active Rhino viewport exactly as you are looking at it. The geometry-free sibling of
/// <see cref="GeometrySnapshot"/> — it runs no generated-geometry scan and never moves the camera, so
/// the button is live the moment the component is wired into the Conversation Log's Human Tools input.
/// That makes it the tool for showing the model referenced geometry Physalia never placed, a view you
/// framed by hand, or simply what is on screen. By default the capture is sent right away as its own
/// user message carrying a built-in default message (editable from the chat window's grounding panel);
/// with "Send With Default Message" unchecked it is instead attached to the prompt box like a pasted
/// image, for you to caption yourself. Has no inputs.
/// </summary>
public class ViewSnapshot : SnapshotToolComponentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ViewSnapshot"/> class.
    /// </summary>
    public ViewSnapshot()
        : base("View Snapshot", "ViewSnap", "Adds a view button to the chat window that captures the active Rhino viewport as-is, with no geometry scan and no camera move — sent as its own message, or attached to the prompt box for you to caption.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("B7E3D14A-9C62-4F05-8A7D-2E6B4C1F93D8");

    /// <inheritdoc/>
    protected override HumanTool Tool => new ViewSnapshotTool(ViewSnapshotTool.DefaultMessage, SendWithMessage);
}
