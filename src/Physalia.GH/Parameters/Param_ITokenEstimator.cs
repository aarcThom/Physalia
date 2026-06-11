// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// A hidden Grasshopper parameter that carries <see cref="GH_ITokenEstimator"/> values between components.
/// </summary>
public class Param_ITokenEstimator : PhyParam<GH_ITokenEstimator>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_ITokenEstimator"/> class.
    /// </summary>
    public Param_ITokenEstimator()
        : base("Token Estimator", "TokEst", "Estimates token consumption for a set of instructions.")
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("ADDFAE36-7B26-4E20-A56C-32B5199B7FB7");
}
