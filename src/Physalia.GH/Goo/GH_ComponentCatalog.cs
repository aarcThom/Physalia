// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using Physalia.Core.Grounding.Components;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapping a <see cref="ComponentCatalog"/> — the snapshot of installed
/// components produced by the Library component and consumed by the Resolver (and, optionally,
/// the Composer for prompt grounding).
/// </summary>
public class GH_ComponentCatalog : PhyGoo<GH_ComponentCatalog, ComponentCatalog>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ComponentCatalog"/> class with no value.
    /// </summary>
    public GH_ComponentCatalog()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_ComponentCatalog"/> class wrapping the given catalog.
    /// </summary>
    /// <param name="catalog">The catalog to wrap.</param>
    public GH_ComponentCatalog(ComponentCatalog catalog)
        : base(catalog)
    {
    }

    /// <inheritdoc/>
    public override string TypeName => "Component Catalog";

    /// <inheritdoc/>
    public override string TypeDescription =>
        "A snapshot of the installed Grasshopper components (names and type GUIDs) used to resolve and ground generated graphs.";

    /// <inheritdoc/>
    public override string ToString() =>
        Value is null ? "(empty component catalog)" : $"Component Catalog ({Value.Count} components)";

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: the Library component rebuilds the catalog from the live
    /// component server, so the goo itself stores nothing.</remarks>
    public override bool Write(GH_IWriter writer) => true;

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: the Library component rebuilds the catalog from the live
    /// component server, so the goo itself stores nothing.</remarks>
    public override bool Read(GH_IReader reader) => true;
}
