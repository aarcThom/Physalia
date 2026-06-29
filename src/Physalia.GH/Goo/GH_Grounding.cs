// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using Physalia.Core.Catalog;
using Physalia.Core.Grounding;

namespace Physalia.GH.Goo;

/// <summary>
/// Grasshopper goo wrapping a <see cref="Grounding"/> — a piece of model-grounding context
/// (today an installed-component catalog; in future a Grasshopper cluster or python function)
/// folded into the system prompt by the Composer.
///
/// <para><see cref="CastFrom"/> adapts the producer goo of each grounding source so existing
/// wiring keeps working without producer changes: a Library's <see cref="GH_ComponentCatalog"/>
/// casts straight into a <see cref="ComponentCatalogGrounding"/> on the Composer's Grounding
/// input, while the Library still emits the raw catalog the Resolver needs.</para>
/// </summary>
public class GH_Grounding : PhyGoo<GH_Grounding, Grounding>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GH_Grounding"/> class with no value.
    /// </summary>
    public GH_Grounding()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GH_Grounding"/> class wrapping the given grounding.
    /// </summary>
    /// <param name="grounding">The grounding to wrap.</param>
    public GH_Grounding(Grounding grounding)
        : base(grounding)
    {
    }

    /// <inheritdoc/>
    public override string TypeName => "Grounding";

    /// <inheritdoc/>
    public override string TypeDescription =>
        "Model-grounding context (component catalog, cluster, or python function) folded into the system prompt.";

    /// <inheritdoc/>
    public override string ToString() =>
        Value is null ? "(empty grounding)" : Value.GetType().Name;

    /// <inheritdoc/>
    public override bool CastFrom(object source)
    {
        switch (source)
        {
            case Grounding grounding:
                Value = grounding;
                return true;
            case GH_Grounding goo:
                Value = goo.Value;
                return true;

            // Adapt the catalog producer (Library) so its existing wire casts onto a Grounding
            // input. The Library still emits GH_ComponentCatalog for the Resolver; only the
            // Composer's input surface changed.
            case GH_ComponentCatalog catalogGoo when catalogGoo.Value is not null:
                Value = new ComponentCatalogGrounding(catalogGoo.Value);
                return true;
            case ComponentCatalog catalog:
                Value = new ComponentCatalogGrounding(catalog);
                return true;

            default:
                return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: groundings are rebuilt live by their producer components,
    /// so the goo itself stores nothing.</remarks>
    public override bool Write(GH_IWriter writer) => true;

    /// <inheritdoc/>
    /// <remarks>Intentional no-op: groundings are rebuilt live by their producer components,
    /// so the goo itself stores nothing.</remarks>
    public override bool Read(GH_IReader reader) => true;
}
