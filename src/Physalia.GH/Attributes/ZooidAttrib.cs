// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Physalia.GH.Attributes;

/// <summary>
/// Custom attributes class for the pyZooid component that intercepts double-click events
/// on the Grasshopper canvas. GH_Component does not expose a virtual OnDoubleClick method,
/// so the standard approach is to subclass GH_ComponentAttributes and override
/// RespondToMouseDoubleClick. The component registers this class via CreateAttributes(),
/// and when the user double-clicks, we delegate to zooid.OpenScriptEditor()
/// to launch the Eto.Forms script editor dialog.
/// </summary>
public class ZooidAttrib : GH_ComponentAttributes
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ZooidAttrib"/> class.
    /// </summary>
    /// <param name="owner">The pyZooid component that owns these attributes.</param>
    public ZooidAttrib(GH_Component owner) : base(owner){ }

    /// <summary>
    /// Intercepts double-click events and opens the script editor when the owner is a pyZooid component.
    /// </summary>
    /// <param name="sender">The Grasshopper canvas that raised the event.</param>
    /// <param name="e">The mouse event data.</param>
    /// <returns>Handled if the script editor was opened; otherwise the base response.</returns>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (Owner is Components.Zooid component)
        {
            component.OpenScriptEditor();
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseDoubleClick(sender, e);
    }
}
