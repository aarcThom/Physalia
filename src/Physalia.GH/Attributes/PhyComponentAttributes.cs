// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Harness;

namespace Physalia.GH.Attributes;

/// <summary>
/// Default attributes for Physalia components that have no custom drawing of their own.
/// Behaves exactly like <see cref="GH_ComponentAttributes"/> except it honours the harness
/// collapse state: when the owning component is a collapsed harness member it shrinks to a
/// point at the proxy Chatbox and draws nothing (see <see cref="CollapseGuard"/>). Components
/// with bespoke attributes call the same guard from their own <c>Layout</c>/<c>Render</c>.
/// </summary>
public class PhyComponentAttributes : GH_ComponentAttributes
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PhyComponentAttributes"/> class.
    /// </summary>
    /// <param name="component">The component that owns these attributes.</param>
    public PhyComponentAttributes(IGH_Component component)
        : base(component)
    {
    }

    /// <inheritdoc/>
    protected override void Layout()
    {
        if (CollapseGuard.TryCollapseLayout(this))
        {
            return;
        }

        base.Layout();
    }

    /// <inheritdoc/>
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (CollapseGuard.IsCollapsed(this))
        {
            return;
        }

        base.Render(canvas, graphics, channel);
    }
}
