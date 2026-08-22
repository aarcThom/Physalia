// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.HumanTools;

namespace Physalia.GH.Components;

/// <summary>
/// Emits an <see cref="ImageMarkUpTool"/> that puts an image editor between every image the human
/// sends and the model: freehand pen, text notes, click-click arrows, and an eraser that takes back
/// mark-up without touching the picture underneath. While it is wired, a capture from any snapshot
/// tool opens in the editor instead of leaving as-is, and any image already in the prompt box grows
/// an edit button on its thumbnail. Has no inputs.
/// <para>
/// The mark-up is flattened into the image on confirm, so nothing about it survives the send — this
/// tool changes what the human can draw, never what the pipeline carries.
/// </para>
/// </summary>
public class ImageMarkUp : HumanToolComponentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImageMarkUp"/> class.
    /// </summary>
    public ImageMarkUp()
        : base("Image Mark Up", "MarkUp", "Opens images in an editor before they are sent, so you can draw on them: pen, text notes, arrows, and an eraser for your marks. Snapshots open in it automatically; pasted images grow an edit button.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("7C1E4B08-6D5A-4F2C-9E37-A4B0D82F6153");

    /// <inheritdoc/>
    protected override string ToolOutputDescription =>
        "Switches the chat window's image editor on. Wire into a Conversation Log's Human Tools input.";

    /// <inheritdoc/>
    protected override HumanTool Tool => new ImageMarkUpTool();
}
