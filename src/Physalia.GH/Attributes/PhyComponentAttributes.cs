// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Physalia.GH.Attributes;

/// <summary>
/// Default attributes for Physalia components that have no custom drawing of their own.
///
/// <para>Behaves exactly like <see cref="GH_ComponentAttributes"/>. It exists as the single seam
/// every Physalia component's attributes descend from, so shared canvas behaviour has one place to
/// live. It used to carry the harness collapse guard, back when a harness was simulated by shrinking
/// its members to a point and skipping their render; harnesses are now real sub-documents, so there
/// is nothing to hide and no guard to apply.</para>
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
}
