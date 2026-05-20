// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Physalia.GH.Components;

internal static class ComponentHelpers
{
    /// <summary>
    /// Creates a dropdown <see cref="GH_ValueList"/>, positions it to the left of the component,
    /// adds it to the document, and wires it to the specified input parameter.
    /// </summary>
    /// <param name="component">The component that owns the target input.</param>
    /// <param name="document">The active Grasshopper document.</param>
    /// <param name="paramIndex">Index of the input parameter to wire the list to.</param>
    /// <param name="placeholder">Label and value for the initial placeholder item.</param>
    /// <param name="xOffset">Horizontal offset from the component pivot (negative = left).</param>
    /// <returns>The newly created value list.</returns>
    internal static GH_ValueList ValueListAdd(
        GH_Component component,
        GH_Document document,
        int paramIndex,
        string placeholder,
        float xOffset = -300f)
    {
        var list = new GH_ValueList();
        list.CreateAttributes();
        list.ListItems.Clear();
        list.ListItems.Add(new GH_ValueListItem(placeholder, "\"\""));
        list.ListMode = GH_ValueListMode.DropDown;
        list.Attributes.Pivot = new PointF(
            component.Attributes.Pivot.X + xOffset,
            component.Attributes.Pivot.Y);

        document.AddObject(list, false);
        component.Params.Input[paramIndex].AddSource(list);

        return list;
    }
}
