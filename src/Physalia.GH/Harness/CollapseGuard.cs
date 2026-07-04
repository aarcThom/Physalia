// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Physalia.GH.Attributes;
using Physalia.GH.Components;

namespace Physalia.GH.Harness;

/// <summary>
/// Shared collapse rendering for Physalia (<see cref="PhyBase"/>) harness members. When a
/// member is collapsed its node and wires must visually disappear into the proxy Chat
/// without the component being moved or removed — its <see cref="GH_Component.Params"/> stay
/// wired and it keeps solving. This helper, called from each Physalia component-attribute's
/// <c>Layout</c>/<c>Render</c>, shrinks the component and every parameter grip to a zero-size
/// rectangle at the shared collapse point (so internal wires become zero-length) and skips
/// drawing entirely.
///
/// <para>The component's <see cref="IGH_Attributes.Pivot"/> is deliberately never touched, so
/// clearing the collapse flag and re-laying out restores the node at its original location with
/// no per-member position bookkeeping.</para>
/// </summary>
internal static class CollapseGuard
{
    /// <summary>
    /// Lays out a collapsed Physalia component: if its owner is a collapsed
    /// <see cref="PhyBase"/>, shrinks the component and all parameter grips to the shared
    /// collapse point and returns true (the caller must then skip its normal layout). Returns
    /// false for an expanded member, leaving normal layout to proceed.
    /// </summary>
    /// <param name="attr">The component attributes being laid out.</param>
    /// <returns>true when the component was collapsed and normal layout should be skipped.</returns>
    public static bool TryCollapseLayout(GH_ComponentAttributes attr)
    {
        if (attr.Owner is not PhyBase { HarnessCollapsed: true } member)
        {
            return false;
        }

        Collapse(attr, member.HarnessCollapsePoint);
        return true;
    }

    /// <summary>
    /// Whether the component owning these attributes is a collapsed harness member, in which
    /// case its <c>Render</c> should draw nothing.
    /// </summary>
    /// <param name="attr">The component attributes being rendered.</param>
    /// <returns>true when the owner is a collapsed <see cref="PhyBase"/>.</returns>
    public static bool IsCollapsed(GH_ComponentAttributes attr) =>
        attr.Owner is PhyBase { HarnessCollapsed: true };

    /// <summary>
    /// Shrinks the component bounds and every input/output parameter grip to a zero-size
    /// rectangle at <paramref name="point"/>. Leaves the component pivot untouched so expand
    /// restores the original layout.
    /// </summary>
    /// <param name="attr">The component attributes to collapse.</param>
    /// <param name="point">The shared collapse point (the proxy Chat pivot).</param>
    private static void Collapse(GH_ComponentAttributes attr, PointF point)
    {
        attr.Bounds = new RectangleF(point, SizeF.Empty);
        CollapseParams(attr.Owner, point);
    }

    /// <summary>
    /// Shrinks every input/output parameter grip of a component to a zero-size rectangle at
    /// <paramref name="point"/>, so wires that read those grips become zero-length. Used both
    /// for Physalia members (via their own attributes) and for non-Physalia members hidden by
    /// <see cref="CollapsedProxyAttributes"/>.
    /// </summary>
    /// <param name="component">The component whose parameter grips collapse.</param>
    /// <param name="point">The shared collapse point (the proxy Chat pivot).</param>
    public static void CollapseParams(IGH_Component component, PointF point)
    {
        var zero = new RectangleF(point, SizeF.Empty);

        foreach (IGH_Param param in component.Params.Input)
        {
            if (param.Attributes is { } pa)
            {
                pa.Bounds = zero;
                pa.Pivot = point;
            }
        }

        foreach (IGH_Param param in component.Params.Output)
        {
            if (param.Attributes is { } pa)
            {
                pa.Bounds = zero;
                pa.Pivot = point;
            }
        }
    }
}
