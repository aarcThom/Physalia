// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_ComponentCatalog"/> values
/// between the Library, Resolver, and Composer components.
/// </summary>
public class Param_ComponentCatalog : PhyParam<GH_ComponentCatalog>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_ComponentCatalog"/> class.
    /// </summary>
    public Param_ComponentCatalog()
        : base("Component Catalog", "Cat", "A snapshot of the installed Grasshopper components, for resolving and grounding generated graphs.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("C1E7A93F-5D24-4A18-B0F6-2E9D4C8B71A5");
}
