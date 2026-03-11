// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Grasshopper.Kernel;

namespace Physalia.GH.ParamTypes;

/// <summary>
/// Grasshopper parameter type that carries a <see cref="LlmProviderGoo"/> between components on the canvas.
/// </summary>
public class LlmProviderGhParam : GH_Param<LlmProviderGoo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LlmProviderGhParam"/> class.
    /// </summary>
    public LlmProviderGhParam()
        : base("LlmProvider", "LLM", "LLM provider and selected model",
               "Physalia", "Core", GH_ParamAccess.item)
    { }

    /// <summary>
    /// Gets the unique identifier for this parameter type.
    /// </summary>
    public override Guid ComponentGuid => new ("DF372577-B98E-4440-9CD5-4001992324C0");

    /// <summary>
    /// Gets the icon bitmap for this parameter type.
    /// </summary>
    protected override System.Drawing.Bitmap Icon => null;

    /// <summary>
    /// Gets the exposure level that controls where this parameter appears in the GH component palette.
    /// </summary>
    public override GH_Exposure Exposure => GH_Exposure.hidden;
}
