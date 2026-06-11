// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_Signal"/> values between components.
/// </summary>
public class Param_Signal : PhyParam<GH_Signal>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_Signal"/> class.
    /// </summary>
    public Param_Signal()
        : base("Signal", "Sig", "A sequence-numbered event consumed exactly once by downstream components.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("3D9C2F84-7A16-4E0B-B5D9-2C8E1A6F4073");
}
