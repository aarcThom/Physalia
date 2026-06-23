// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Physalia.GH.Components.Utility;

namespace Physalia.GH.Components;

internal static class ComponentHelpers
{
    /// <summary>
    /// Applies the canvas "Draw Full Names" setting to freshly placed objects so their
    /// parameter labels match the rest of the canvas. Grasshopper stores the displayed label
    /// in each parameter's NickName and rewrites every nickname when the setting is toggled
    /// (GH_Document.ConvertNickNamesToFullNames / ConvertFullNamesToNickNames); objects placed
    /// programmatically miss that pass, so we apply the current state here. When full names are
    /// off, the default abbreviated nicknames are left untouched — a later toggle is handled by
    /// Grasshopper's own document-wide conversion.
    /// </summary>
    /// <param name="objects">The freshly placed document objects.</param>
    internal static void ApplyNickNameDisplay(IEnumerable<IGH_DocumentObject> objects)
    {
        if (!CentralSettings.CanvasFullNames)
        {
            return;
        }

        foreach (IGH_DocumentObject obj in objects)
        {
            ExpandToFullName(obj);
        }
    }

    /// <summary>
    /// Applies the canvas "Draw Full Names" setting to a single freshly placed object.
    /// </summary>
    /// <param name="obj">The freshly placed document object.</param>
    internal static void ApplyNickNameDisplay(IGH_DocumentObject obj)
    {
        if (CentralSettings.CanvasFullNames)
        {
            ExpandToFullName(obj);
        }
    }

    // Sets each parameter's NickName to its full Name — the state GH itself puts the document in
    // while "Draw Full Names" is on. The attributes' layout is then expired so the next layout pass
    // recomputes capsule widths to fit the full names: objects placed by GhJSON's Put were already
    // laid out with the short JSON nicknames, so without this the label is truncated ("Si...") to
    // the stale narrow width.
    private static void ExpandToFullName(IGH_DocumentObject obj)
    {
        if (obj is IGH_Component comp)
        {
            foreach (IGH_Param param in comp.Params.Input)
            {
                param.NickName = param.Name;
            }

            foreach (IGH_Param param in comp.Params.Output)
            {
                param.NickName = param.Name;
            }
        }
        else if (obj is IGH_Param floatingParam)
        {
            floatingParam.NickName = floatingParam.Name;
        }

        obj.Attributes?.ExpireLayout();
    }

    /// <summary>
    /// Creates a <see cref="Picker"/> component, positions it to the left of the component,
    /// adds it to the document, and wires its output to the specified input parameter.
    /// The component must implement <see cref="IPickableValues"/> so the Picker can
    /// discover the available values at solve time.
    /// </summary>
    /// <param name="component">The component that owns the target input.</param>
    /// <param name="document">The active Grasshopper document.</param>
    /// <param name="paramIndex">Index of the input parameter to wire the picker to.</param>
    /// <param name="xOffset">Horizontal offset from the component pivot (negative = left).</param>
    /// <param name="yOffset">Vertical offset from the component pivot (positive = down).</param>
    /// <returns>The newly created picker.</returns>
    internal static Picker PickerAdd(
        GH_Component component,
        GH_Document document,
        int paramIndex,
        float xOffset = -200f,
        float yOffset = 0f)
    {
        var picker = new Picker();
        picker.CreateAttributes();
        picker.Attributes.Pivot = new PointF(
            component.Attributes.Pivot.X + xOffset,
            component.Attributes.Pivot.Y + yOffset);

        document.AddObject(picker, false);
        ApplyNickNameDisplay(picker);
        component.Params.Input[paramIndex].AddSource(picker.Params.Output[0]);

        return picker;
    }
}
