// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;
using Physalia.GH.Goo;

namespace Physalia.GH.Parameters;

/// <summary>
/// Grasshopper parameter for passing <see cref="GH_ModelConfig"/> between components.
/// </summary>
public class Param_ModelConfig : GH_Param<GH_ModelConfig>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Param_ModelConfig"/> class.
    /// </summary>
    public Param_ModelConfig()
        : base("Model Config", "Model", "An LLM model configuration.", "Physalia", "Params", GH_ParamAccess.item)
    {
    }

    /// <inheritdoc/>
    public override Guid ComponentGuid => new Guid("DBC83177-9779-4BC1-B2F9-E754371D8757");

    /// <inheritdoc/>
    public override GH_Exposure Exposure => GH_Exposure.hidden;
}
