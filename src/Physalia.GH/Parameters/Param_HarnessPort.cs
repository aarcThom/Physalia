// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;

namespace Physalia.GH.Parameters;

/// <summary>
/// The INSIDE end of a harness port: a Harness In's output, or a Harness Out's input. One type for
/// both, because both are the same thing seen from opposite directions — the parameter a user wires
/// to in here, whose name labels what the harness shows out there.
///
/// <para>It exists as its own type so a rename can be seen. For a Harness In that name is shared with
/// a real parameter on the proxy and the sync runs both ways; for a Harness Out the proxy end is a
/// label painted beside a drag grip, so the name travels outward only. Either way the hook is
/// <see cref="Param_LinkedName"/>, and either way it starts out "Data".</para>
/// </summary>
public class Param_HarnessPort : Param_LinkedName
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_HarnessPort"/> class.
    /// </summary>
    public Param_HarnessPort()
    {
        Access = GH_ParamAccess.tree;
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("6D4C0B18-2E93-4A71-8F5C-3B90A7E162D5");

    /// <inheritdoc/>
    /// <remarks>Never placed by hand — it only means anything on a Harness In or a Harness Out.</remarks>
    public override GH_Exposure Exposure => GH_Exposure.hidden;
}
