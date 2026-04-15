// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes class for the Receiver component that intercepts double-click events
/// on the Grasshopper canvas. GH_Component does not expose a virtual OnDoubleClick method,
/// so the standard approach is to subclass GH_ComponentAttributes and override
/// RespondToMouseDoubleClick. The component registers this class via CreateAttributes(),
/// and when the user double-clicks, we delegate to receiver.OpenEditor()
/// to launch the Eto.Forms script editor dialog.
/// </summary>
public class ReceiverAttrib : GH_ComponentAttributes
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReceiverAttrib"/> class.
    /// </summary>
    /// <param name="owner">The Receiver component that owns these attributes.</param>
    public ReceiverAttrib(GH_Component owner) : base(owner){ }

    /// <summary>
    /// Intercepts double-click events and opens the editor when the owner is a ReceiverBase component.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if the editor was opened; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (Owner is Components.ReceiverBase receiver)
        {
            receiver.OpenEditor();
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseDoubleClick(sender, e);
    }
}
