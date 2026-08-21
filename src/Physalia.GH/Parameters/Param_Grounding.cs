// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_Grounding"/> values into the
/// System Prompt's Grounding input. Accepts any grounding producer's goo via
/// <see cref="GH_Grounding.CastFrom"/>.
/// </summary>
public class Param_Grounding : PhyParam<GH_Grounding>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_Grounding"/> class.
    /// </summary>
    public Param_Grounding()
        : base("Grounding", "Gnd", "Something the model should know about this document, folded into its instructions.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("F3A1C7D2-4B8E-4A16-9C3D-5E2F8B0A1D74");
}
