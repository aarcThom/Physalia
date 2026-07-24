// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.Core.HumanTools;

namespace Physalia.GH.Components;

/// <summary>
/// Emits an <see cref="AddImageTool"/> that enables image attachments in the chat window's prompt
/// box: while it is wired into the Conversation Log's Human Tools input, images can be pasted,
/// dragged in, or picked from disk and ride the submitted prompt as content blocks. Without it,
/// image intake is fully disabled and prompts are text-only. Has no inputs.
/// </summary>
public class AddImage : HumanToolComponentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddImage"/> class.
    /// </summary>
    public AddImage()
        : base("Add Image", "AddImg", "Enables image attachments (paste, drag-drop, file picker) in the chat window's prompt box.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("4F7A9C25-8D13-4E6B-A2C9-1B5E8F3D7A60");

    /// <inheritdoc/>
    protected override HumanTool Tool => new AddImageTool();
}
