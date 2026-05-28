// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using Grasshopper.Kernel;
using Physalia.GH.Components.Utility;

namespace Physalia.GH.Components;

internal static class ComponentHelpers
{
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
        component.Params.Input[paramIndex].AddSource(picker.Params.Output[0]);

        return picker;
    }
}
