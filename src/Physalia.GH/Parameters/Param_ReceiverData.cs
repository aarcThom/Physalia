// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;

namespace Physalia.GH.Parameters;

/// <summary>
/// A Receiver's output — the inside end of a harness inlet.
///
/// <para>It exists as its own type so a rename can be seen. This nickname and the nickname of the
/// input the Receiver grows on the harness proxy are ONE name: rename either and the other follows,
/// which is what <see cref="Param_LinkedName"/> provides. Both start out "Data".</para>
/// </summary>
public class Param_ReceiverData : Param_LinkedName
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_ReceiverData"/> class.
    /// </summary>
    public Param_ReceiverData()
    {
        Access = GH_ParamAccess.tree;
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("6D4C0B18-2E93-4A71-8F5C-3B90A7E162D5");

    /// <inheritdoc/>
    /// <remarks>Never placed by hand — it only means anything as a Receiver's output.</remarks>
    public override GH_Exposure Exposure => GH_Exposure.hidden;
}
