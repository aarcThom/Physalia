// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// Grasshopper parameter for passing <see cref="GH_Instructions"/> between components.
/// </summary>
public class Param_Instructions : PhyParam<GH_Instructions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_Instructions"/> class.
    /// </summary>
    public Param_Instructions()
        : base("Instructions", "Instructions", "Everything one call to a model needs: the standing instructions and the conversation so far.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("F2A94B7E-3C61-4D08-B5E2-9A1F6C0D3E87");
}
